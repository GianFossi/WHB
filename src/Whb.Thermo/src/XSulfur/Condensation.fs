namespace XSulfur

open System
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.TwoPhase

/// Sulfur-side condensation: the generic two-phase methods applied with
/// sulfur's own chemistry.
///
/// Silver / Bell & Ghaly and Colburn & Hougen are NOT sulfur methods. They live
/// in WhbThermo.TwoPhase and are used unchanged here. Duplicating them into this
/// library would give two implementations that drift apart, which is exactly the
/// failure this consolidation exists to prevent. What IS sulfur-specific is what
/// feeds them: the dew point, the allotrope distribution, the polymerisation
/// enthalpy and the liquid film. Those come from the modules above.
module Condensation =

    /// Gas-phase mass transfer coefficient from the sensible heat transfer
    /// coefficient, via the Chilton-Colburn analogy (j_H = j_D):
    ///
    ///     k_G = h_G / (rho cp Le^(2/3))
    ///
    /// In a Claus condenser N2, CO2 and H2O are 80 to 90 % of the gas, so the
    /// controlling resistance is diffusion of sulfur through them, not the
    /// liquid film. That makes k_G the number the rating turns on, and deriving
    /// it from the measured-or-correlated h_G is more defensible than estimating
    /// a diffusivity for a polymerising vapour.
    let gasMassTransferFromHtc (gasCoefficient: float<W/(m^2*K)>) (gasDensity: float)
                               (gasHeatCapacity: float) (lewisNumber: float)
                               : Thermo<float> =
        if float gasCoefficient <= 0.0 then
            fail (InvalidMixture "the gas film coefficient must be positive")
        elif gasDensity <= 0.0 || gasHeatCapacity <= 0.0 then
            fail (InvalidMixture "the gas properties must be positive")
        elif lewisNumber <= 0.0 then
            fail (InvalidMixture "the Lewis number must be positive")
        else
            ok (float gasCoefficient / (gasDensity * gasHeatCapacity * (lewisNumber ** (2.0 / 3.0))))

    /// Effective coefficient by Silver / Bell & Ghaly, with Z taken from the
    /// sulfur condensation path.
    ///
    /// The polymerisation enthalpy belongs in the heat release that sets dT/dh.
    /// Omitting it makes the curve look steeper than it is, which understates Z,
    /// which overstates the effective coefficient - the error compounds in the
    /// non-conservative direction at every step.
    let effectiveCoefficient (filmCoefficient: float<W/(m^2*K)>)
                             (gasCoefficient: float<W/(m^2*K)>)
                             (vapourFraction: float) (vapourHeatCapacity: float)
                             (temperatureSlope: float) : Thermo<float<W/(m^2*K)>> =
        WhbThermo.TwoPhase.Condensation.zFactor vapourFraction vapourHeatCapacity temperatureSlope
        >>= fun z ->
            WhbThermo.TwoPhase.Condensation.silverBellGhaly filmCoefficient gasCoefficient z

    /// Colburn & Hougen interface temperature, closed against the sulfur vapour
    /// pressure curve.
    ///
    /// The generic solver takes the saturation pressure as a function so the
    /// caller owns the flash; for sulfur that function is `Chemistry.vapourPressure`,
    /// so it can be supplied here rather than asked for.
    let interfaceTemperature (model: Speciation.Model)
                             (gasCoefficient: float) (filmCoefficient: float)
                             (massTransferCoefficient: float)
                             (totalPressure: float) (bulkSulfurPressure: float)
                             (latentHeat: float)
                             (tGas: float<K>) (tWall: float<K>) : Thermo<float<K>> =
        // Mean molar mass of the condensing vapour, from the equilibrium at the
        // wall - the allotrope mix at the interface is not the bulk mix.
        Speciation.distribution model tWall (bulkSulfurPressure / 1.0e5 * 1.0<bar>)
        >>= fun atWall ->
            let saturation (t: float<K>) =
                match Chemistry.vapourPressure model t with
                | Success (p, _) -> float p * 1.0e5      // bar -> Pa
                | Failure _ -> nan

            WhbThermo.TwoPhase.Condensation.solveColburnHougen
                gasCoefficient filmCoefficient massTransferCoefficient
                totalPressure bulkSulfurPressure saturation
                (atWall.AverageMolarMass / 1000.0) latentHeat tGas tWall

    /// Heat of condensation per kg of sulfur at a temperature, INCLUDING the
    /// polymerisation contribution.
    ///
    /// Taken as the difference between the equilibrium vapour enthalpy per
    /// sulfur atom and the liquid enthalpy per atom, so the ring-opening and
    /// chain-forming terms are inside it by construction. A bare S8 latent heat
    /// omits them and understates the duty in the polymerising range, which is
    /// the range a condenser inlet sits in.
    let heatOfCondensation (model: Speciation.Model) (t: float<K>)
                           (sulfurPressure: float<bar>) : Thermo<float> =
        match model.Liquid with
        | None ->
            fail (DatabaseParseError
                    ("the sulfur database has no liquid reference phase, so the heat of "
                     + "condensation cannot be derived; re-run tools/import_sulfur.py"))
        | Some liquid ->
            Speciation.distribution model t sulfurPressure
            >>= fun d ->
                let vapourPerAtom = d.EnthalpyPerAtom
                let liquidPerAtom = Speciation.enthalpy liquid t / float liquid.Atoms
                let perMoleAtom = vapourPerAtom - liquidPerAtom
                if perMoleAtom <= 0.0 then
                    fail (CorrelationExtrapolated
                            ("sulfur heat of condensation",
                             $"non-positive at %.1f{float t - 273.15} degC"))
                else
                    // J per mole of atoms -> J per kg of sulfur.
                    ok (perMoleAtom / (model.AtomicMass / 1000.0))
