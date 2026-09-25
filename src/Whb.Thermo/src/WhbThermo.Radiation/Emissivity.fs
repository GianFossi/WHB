namespace WhbThermo.Radiation

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Participating-gas emissivity, Leckner (1972) formulation.
///
/// Functional form (Leckner 1972; reproduced in Modest, *Radiative Heat Transfer*,
/// ch. 10, and in VDI-Waermeatlas):
///
///     log10 eps0 = a0 + SUM(i=1..M) a_i * (log10 pL)^i
///     a_i        = c[0,i] + SUM(j=1..N) c[j,i] * (T/1000)^j
///
/// so the whole model reduces to one (N+1) x (M+1) coefficient matrix per species.
/// M and N differ between H2O and CO2, which is exactly why the matrix is DATA:
/// its shape is read from JSON, not baked into the code.
///
/// IMPORTANT: the coefficient matrices shipped with this repository are EMPTY.
/// They must be transcribed from the source before gas radiation can be evaluated.
/// Until then every call returns Failure - never a plausible-looking wrong number.
module Emissivity =

    /// Stefan-Boltzmann constant.
    let sigma = PhysicalConstants.StefanBoltzmann * 1.0<W/(m^2*K^4)>

    type LecknerCoefficients =
        { /// c.[j].[i] : row j = temperature power, column i = log10(pL) power.
          C            : float[][]
          /// Validity window.
          TMin         : float<K>
          TMax         : float<K>
          /// Partial-pressure-path-length window [bar*m].
          PathMin      : float
          PathMax      : float
          Source       : string }

        member this.IsPopulated =
            this.C |> Array.exists (fun row -> row |> Array.exists (fun v -> v <> 0.0))

    /// Overlap correction between the H2O and CO2 bands:
    ///
    ///   delta_eps = zeta (1 - zeta) * SUM_j SUM_m [ C[j][m] + CAsym[j][m] * zeta ]
    ///                                 * theta^j * (log10 pL_tot)^m
    ///
    /// with zeta = p_H2O / (p_H2O + p_CO2) and theta = T/1000.
    ///
    /// The zeta(1-zeta) prefactor makes the correction vanish exactly when either
    /// species is absent -- a boundary condition worth enforcing structurally
    /// rather than hoping a fit reproduces it. CAsym carries the asymmetry about
    /// zeta = 0.5, which is significant at CO2-rich compositions.
    type OverlapCoefficients =
        { C      : float[][]
          CAsym  : float[][]
          Source : string }

        member this.IsPopulated =
            this.C |> Array.exists (fun row -> row |> Array.exists (fun v -> v <> 0.0))

    type RadiationModel =
        { Water   : LecknerCoefficients
          Dioxide : LecknerCoefficients
          Overlap : OverlapCoefficients }

    // ---------- mean beam length ----------

    type Geometry =
        /// Gas inside a tube of internal diameter D. Le = 0.95 * D (manual s4).
        | CircularDuct of dInt: float<m>
        /// Arbitrary volume-to-area geometry. Le = 3.6 * V / A.
        | VolumeToArea of volume: float<m^3> * area: float<m^2>

    let meanBeamLength geometry : float<m> =
        match geometry with
        | CircularDuct d -> 0.95 * d
        | VolumeToArea (v, a) -> 3.6 * v / a

    // ---------- Leckner evaluation ----------

    /// eps0 = 10 ^ [ SUM(i) a_i (log10 pL)^i ],  a_i = SUM(j) c[j][i] (T/1000)^j
    let private evaluatePolynomial (c: float[][]) (t: float<K>) (pathBarM: float) =
        let theta = float t / 1000.0
        let logPl = log10 pathBarM
        let columns = if c.Length = 0 then 0 else c.[0].Length
        let mutable acc = 0.0
        for i in 0 .. columns - 1 do
            let mutable ai = 0.0
            for j in 0 .. c.Length - 1 do
                ai <- ai + c.[j].[i] * (theta ** float j)
            acc <- acc + ai * (logPl ** float i)
        10.0 ** acc

    let private evaluateSpecies (name: string) (coeff: LecknerCoefficients)
                                (t: float<K>) (pathBarM: float) : Thermo<float> =
        if not coeff.IsPopulated then
            fail (RadiationCoefficientsPending ("Leckner", name))
        elif pathBarM <= 0.0 then
            ok 0.0
        else
            let eps = evaluatePolynomial coeff.C t pathBarM
            ok (max 0.0 (min 1.0 eps))
            |> warnIf (t < coeff.TMin || t > coeff.TMax)
                      (OutsideFitRange (name, "Leckner emissivity", float t,
                                        float coeff.TMin, float coeff.TMax))
            |> warnIf (pathBarM < coeff.PathMin || pathBarM > coeff.PathMax)
                      (CorrelationExtrapolated ("Leckner",
                        $"p*L = %.4f{pathBarM} bar*m outside %.4f{coeff.PathMin}-%.1f{coeff.PathMax}"))

    /// Total gas emissivity: eps_g = eps_H2O + eps_CO2 - delta_eps_overlap.
    ///
    /// `pH2O` and `pCO2` are PARTIAL pressures [bar]; `le` is the mean beam length.
    let gasEmissivity (model: RadiationModel) (t: float<K>)
                      (pH2O: float<bar>) (pCO2: float<bar>) (le: float<m>) : Thermo<float> =
        let pathW = float pH2O * float le
        let pathC = float pCO2 * float le

        if pathW <= 0.0 && pathC <= 0.0 then
            ok 0.0 |> warn NoRadiatingSpecies
        else
            let water = if pathW > 0.0 then evaluateSpecies "H2O" model.Water t pathW else ok 0.0
            let dioxide = if pathC > 0.0 then evaluateSpecies "CO2" model.Dioxide t pathC else ok 0.0

            water
            >>= fun ew ->
                dioxide
                >>= fun ec ->
                    if pathW > 0.0 && pathC > 0.0 then
                        if not model.Overlap.IsPopulated then
                            fail (RadiationCoefficientsPending ("Leckner overlap", "H2O+CO2"))
                        else
                            let total = pathW + pathC
                            let zeta = pathW / total
                            let theta = float t / 1000.0
                            let logPl = log10 total
                            let asym = model.Overlap.CAsym

                            let mutable sum = 0.0
                            for j in 0 .. model.Overlap.C.Length - 1 do
                                let row = model.Overlap.C.[j]
                                let rowAsym =
                                    if isNull asym || j >= asym.Length then Array.empty else asym.[j]
                                for m in 0 .. row.Length - 1 do
                                    let a = if m < rowAsym.Length then rowAsym.[m] else 0.0
                                    sum <- sum + (row.[m] + a * zeta)
                                                 * (theta ** float j) * (logPl ** float m)

                            let deltaEps = zeta * (1.0 - zeta) * sum
                            ok (max 0.0 (min 1.0 (ew + ec - deltaEps)))
                    else
                        ok (max 0.0 (min 1.0 (ew + ec)))

    // ---------- radiative film coefficient ----------

    /// Effective emissivity of the gas-to-wall exchange (manual s4):
    ///   eps_eff = [ 1/eps_g + 1/eps_tube - 1 ]^-1
    let effectiveEmissivity (epsGas: float) (epsTube: float) =
        if epsGas <= 0.0 then 0.0
        else 1.0 / (1.0 / epsGas + 1.0 / epsTube - 1.0)

    /// Linearised radiative film coefficient [W/(m^2*K)]:
    ///   h_rad = eps_eff * sigma * (Tg^4 - Tw^4) / (Tg - Tw)
    /// The removable singularity at Tg = Tw is handled analytically
    /// (limit -> 4 * eps_eff * sigma * T^3), so the solver never divides by zero
    /// as the tube approaches thermal equilibrium.
    let radiativeCoefficient (epsEff: float) (tGas: float<K>) (tWall: float<K>) : float<W/(m^2*K)> =
        let tg = float tGas
        let tw = float tWall
        if abs (tg - tw) < 1e-6 then
            4.0 * epsEff * float sigma * tg ** 3.0 * 1.0<W/(m^2*K)>
        else
            epsEff * float sigma * (tg ** 4.0 - tw ** 4.0) / (tg - tw) * 1.0<W/(m^2*K)>
