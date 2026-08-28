namespace Whb.Core

open System
open Constants

module Claus =

    type Mode =
        | Frozen
        | Equilibrium
        | Kinetic

    type Arrhenius =
        { PreExponential: float
          ActivationEnergy: float }

    type KineticParameters =
        { SeverityFactor: float
          TauFactor: float
          SubSteps: int
          Claus: Arrhenius
          CosHydrolysis: Arrhenius
          Cs2Hydrolysis: Arrhenius }

    let defaultKineticParameters =
        { SeverityFactor = 0.15
          TauFactor = 0.35
          SubSteps = 8
          Claus =
            { PreExponential = 5.0e5
              ActivationEnergy = 6.0e4 }
          CosHydrolysis =
            { PreExponential = 2.0e5
              ActivationEnergy = 7.0e4 }
          Cs2Hydrolysis =
            { PreExponential = 3.0e5
              ActivationEnergy = 9.0e4 } }

    let modeName =
        function
        | Frozen -> "congelata (nessuna conversione Claus)"
        | Equilibrium -> "chiusura Claus istantanea locale"
        | Kinetic -> "cinetica Claus semplificata lungo il fascio"

    let sanitizeKineticParameters (parameters: KineticParameters) =
        let clampRate (rate: Arrhenius) =
            { PreExponential = max 0.0 rate.PreExponential
              ActivationEnergy = max 0.0 rate.ActivationEnergy }
        { SeverityFactor = max 0.0 parameters.SeverityFactor
          TauFactor = max 0.0 parameters.TauFactor
          SubSteps = max 1 parameters.SubSteps
          Claus = clampRate parameters.Claus
          CosHydrolysis = clampRate parameters.CosHydrolysis
          Cs2Hydrolysis = clampRate parameters.Cs2Hydrolysis }

    let withSeverity (severityFactor: float) (parameters: KineticParameters) =
        { sanitizeKineticParameters parameters with
            SeverityFactor = max 0.0 severityFactor }

    let hasReactiveSpecies (composition: GasProps.Composition) =
        let comp = GasProps.normalize composition
        [ GasProps.H2S; GasProps.SO2; GasProps.COS; GasProps.CS2 ]
        |> List.exists (fun sp -> GasProps.molFrac comp sp > 1e-10)

    let elementalSulphurAtomFraction (composition: GasProps.Composition) =
        let comp = GasProps.normalize composition
        let sulphurAtoms =
            [ GasProps.H2S, 1.0
              GasProps.SO2, 1.0
              GasProps.COS, 1.0
              GasProps.CS2, 2.0
              GasProps.S2, 2.0
              GasProps.S6, 6.0
              GasProps.S8, 8.0 ]
            |> List.sumBy (fun (sp, atoms) -> atoms * GasProps.molFrac comp sp)
        if sulphurAtoms <= 1e-12 then 0.0
        else
            (2.0 * GasProps.molFrac comp GasProps.S2
             + 6.0 * GasProps.molFrac comp GasProps.S6
             + 8.0 * GasProps.molFrac comp GasProps.S8) / sulphurAtoms

    let private speciesAmount (sp: GasProps.Species) (composition: GasProps.Composition) =
        composition
        |> List.tryPick (fun (s, n) -> if s = sp then Some n else None)
        |> Option.defaultValue 0.0

    let private setSpecies (sp: GasProps.Species) (value: float) (composition: GasProps.Composition) =
        let v = max 0.0 value
        if composition |> List.exists (fun (s, _) -> s = sp) then
            composition |> List.map (fun (s, n) -> if s = sp then (s, v) else (s, n))
        else
            (sp, v) :: composition

    let private applyExtent (stoich: (GasProps.Species * float) list) (xi: float) (composition: GasProps.Composition) =
        (composition, stoich)
        ||> List.fold (fun acc (sp, nu) ->
            setSpecies sp (speciesAmount sp acc + nu * xi) acc)

    let private rateFactor (mode: Mode) (parameters: KineticParameters) (rate: Arrhenius) (tK: float) (tauS: float) =
        match mode with
        | Frozen -> 0.0
        | Equilibrium -> 1.0
        | Kinetic ->
            let k =
                parameters.SeverityFactor
                * rate.PreExponential
                * exp (-rate.ActivationEnergy / (Constants.R * max 250.0 tK))
            1.0 - exp (-max 0.0 (k * max 0.0 tauS))

    let private hydrolyseCos (mode: Mode) (parameters: KineticParameters) (tK: float) (tauS: float) (composition: GasProps.Composition) =
        let nCos = speciesAmount GasProps.COS composition
        let nH2O = speciesAmount GasProps.H2O composition
        let xiMax = min nCos nH2O
        if xiMax <= 1e-12 then composition
        else
            let steamFactor = min 2.0 (nH2O / max 1e-12 nCos)
            let f = min 1.0 (steamFactor * rateFactor mode parameters parameters.CosHydrolysis tK tauS)
            applyExtent
                [ GasProps.COS, -1.0
                  GasProps.H2O, -1.0
                  GasProps.H2S, 1.0
                  GasProps.CO2, 1.0 ]
                (xiMax * f) composition

    let private hydrolyseCs2 (mode: Mode) (parameters: KineticParameters) (tK: float) (tauS: float) (composition: GasProps.Composition) =
        let nCs2 = speciesAmount GasProps.CS2 composition
        let nH2O = speciesAmount GasProps.H2O composition
        let xiMax = min nCs2 (0.5 * nH2O)
        if xiMax <= 1e-12 then composition
        else
            let steamFactor = min 2.0 (nH2O / max 1e-12 (2.0 * nCs2))
            let f = min 1.0 (steamFactor * rateFactor mode parameters parameters.Cs2Hydrolysis tK tauS)
            applyExtent
                [ GasProps.CS2, -1.0
                  GasProps.H2O, -2.0
                  GasProps.H2S, 2.0
                  GasProps.CO2, 1.0 ]
                (xiMax * f) composition

    let private reactClaus (mode: Mode) (parameters: KineticParameters) (tK: float) (tauS: float) (composition: GasProps.Composition) =
        let nH2S = speciesAmount GasProps.H2S composition
        let nSO2 = speciesAmount GasProps.SO2 composition
        let xiMax = min nSO2 (0.5 * nH2S)
        if xiMax <= 1e-12 then composition
        else
            let f = rateFactor mode parameters parameters.Claus tK tauS |> min 1.0
            applyExtent
                [ GasProps.H2S, -2.0
                  GasProps.SO2, -1.0
                  GasProps.S2, 1.5
                  GasProps.H2O, 2.0 ]
                (xiMax * f) composition

    let private trim (composition: GasProps.Composition) =
        composition
        |> List.filter (fun (_, n) -> n > 1e-12)
        |> GasProps.normalize

    let advanceWith (parameters: KineticParameters) (mode: Mode) (tK: float) (tauS: float) (composition: GasProps.Composition) =
        let p = sanitizeKineticParameters parameters
        let comp0 = GasProps.normalize composition
        match mode with
        | Frozen -> comp0
        | _ ->
            let subSteps =
                match mode with
                | Kinetic -> p.SubSteps
                | _ -> 1
            let dt = p.TauFactor * tauS / float subSteps
            let mutable comp = comp0
            for _ in 1 .. subSteps do
                comp <- hydrolyseCos mode p tK dt comp
                comp <- hydrolyseCs2 mode p tK dt comp
                comp <- reactClaus mode p tK dt comp
                comp <- trim comp
            comp

    let advance (mode: Mode) (tK: float) (tauS: float) (composition: GasProps.Composition) =
        advanceWith defaultKineticParameters mode tK tauS composition

    let calibrateSeverity (targetElementalSulphurFraction: float) (parameters: KineticParameters)
                          (tK: float) (tauS: float) (composition: GasProps.Composition) =
        let target = max 0.0 (min 0.999999 targetElementalSulphurFraction)
        if target <= 0.0 then withSeverity 0.0 parameters
        else
            let response severity =
                advanceWith (withSeverity severity parameters) Kinetic tK tauS composition
                |> elementalSulphurAtomFraction
            let mutable hi = max 1.0 (sanitizeKineticParameters parameters).SeverityFactor
            let mutable yHi = response hi
            let mutable it = 0
            while yHi < target && it < 24 do
                hi <- 2.0 * hi
                yHi <- response hi
                it <- it + 1
            if yHi <= target then withSeverity hi parameters
            else
                let severity = bisect (fun s -> response s - target) 0.0 hi 1e-6 80
                withSeverity severity parameters
