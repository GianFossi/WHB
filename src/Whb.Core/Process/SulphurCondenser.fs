namespace Whb.Core

open System
open Constants

module SulphurCondenser =

    type Feed =
        { Composition: GasProps.Composition
          MassFlow: float
          TIn: float
          PIn: float
          Z: float
          ShiftMode: Shift.Mode
          ClausMode: Claus.Mode
          ClausKinetics: Claus.KineticParameters
          MixingRule: GasProps.MixingRule
          RealGas: bool }

    type Spec =
        { Enabled: bool
          UseWhbOutlet: bool
          Sections: int
          ResidenceTime: float
          DpTotal: float
          TOutTarget: float
          TWall: float
          TCoolant: float
          UAssumed: float
          Feed: Feed }

    type Segment =
        { Index: int
          TIn: float
          TOut: float
          PIn: float
          POut: float
          Duty: float
          DutyLatent: float
          DutySensible: float
          AreaRequired: float
          YElementalSulphurIn: float
          YElementalSulphurOut: float
          CondensedFractionIn: float
          CondensedFractionOut: float
          SulphurDewPointIn: float option
          SulphurDewPointOut: float option }

    type Result =
        { SpecUsed: Spec
          SourceLabel: string
          FeedUsed: Feed
          InletState: Sulphur.ProcessState
          OutletState: Sulphur.ProcessState
          OutletComposition: GasProps.Composition
          Segments: Segment list
          Duty: float
          DutyLatent: float
          DutySensible: float
          AreaRequired: float
          CondensedSulphurAtomsFlow: float
          CondensedSulphurMassFlow: float
          SteamPressureForWall: float
          Fog: Sulphur.FogAssessment
          Checks: Sulphur.Check list }

    let private sulphurAtomMolarMass = GasProps.molarMass GasProps.S8 / 8.0

    let sanitizeFeed (feed: Feed) =
        { Composition = GasProps.normalize feed.Composition
          MassFlow = max 0.0 feed.MassFlow
          TIn = max (cToK 50.0) feed.TIn
          PIn = max 1.0e4 feed.PIn
          Z = max 0.1 feed.Z
          ShiftMode = feed.ShiftMode
          ClausMode = feed.ClausMode
          ClausKinetics = Claus.sanitizeKineticParameters feed.ClausKinetics
          MixingRule = feed.MixingRule
          RealGas = feed.RealGas }

    let sanitizeSpec (spec: Spec) =
        { Enabled = spec.Enabled
          UseWhbOutlet = spec.UseWhbOutlet
          Sections = max 1 spec.Sections
          ResidenceTime = max 0.0 spec.ResidenceTime
          DpTotal = max 0.0 spec.DpTotal
          TOutTarget = spec.TOutTarget
          TWall = spec.TWall
          TCoolant = spec.TCoolant
          UAssumed = max 1.0 spec.UAssumed
          Feed = sanitizeFeed spec.Feed }

    let private processStateAt (feed: Feed) (pPa: float) (composition: GasProps.Composition) (tK: float) =
        if Sulphur.hasElementalSulphur composition then
            Sulphur.processStateAt feed.ShiftMode feed.RealGas pPa composition tK
        else
            let compGas = Shift.equilibrate feed.ShiftMode composition tK
            let st : Sulphur.ProcessState =
                { T = tK
                  VapourComposition = compGas
                  TotalSpecificEnthalpy = GasProps.enthalpyAbsReal feed.RealGas compGas tK pPa
                  CpApprox = (GasProps.mixReal feed.MixingRule feed.RealGas compGas tK pPa feed.Z).Cp
                  PSulphur = 0.0
                  YElementalSulphurVapour = 0.0
                  SulphurDewPoint = None
                  Condensing = false
                  CondensedAtoms = 0.0
                  CondensedFraction = 0.0 }
            st

    let private lmtdPositive (dt1: float) (dt2: float) =
        let a = max 1.0e-6 dt1
        let b = max 1.0e-6 dt2
        if abs (a - b) <= 1.0e-9 then a else (a - b) / log (a / b)

    let private totalMolarFlow (massFlow: float) (composition: GasProps.Composition) =
        massFlow / max 1.0e-12 (GasProps.mixMolarMass (GasProps.normalize composition))

    let solveWithFeed (sourceLabel: string) (specIn: Spec) (feedIn: Feed) =
        let spec = sanitizeSpec { specIn with Feed = feedIn }
        let feed = sanitizeFeed feedIn
        let tOutTarget = min feed.TIn spec.TOutTarget
        let dtTotal = feed.TIn - tOutTarget
        let dpStep = spec.DpTotal / float spec.Sections
        let tauStep = spec.ResidenceTime / float spec.Sections
        let mutable comp = GasProps.normalize feed.Composition
        let mutable tNow = feed.TIn
        let mutable pNow = feed.PIn
        let inletState = processStateAt feed pNow comp tNow
        let segments = ResizeArray<Segment>()
        let mutable dutyTotal = 0.0
        let mutable latentTotal = 0.0
        let mutable sensibleTotal = 0.0
        let mutable areaTotal = 0.0
        let mutable stateNow = inletState
        for i in 1 .. spec.Sections do
            let frac = float i / float spec.Sections
            let tNext = feed.TIn - frac * dtTotal
            let pNext = max 1.0e4 (feed.PIn - float i * dpStep)
            let tRef = 0.5 * (tNow + tNext)
            let compNext =
                if feed.ClausMode = Claus.Frozen then comp
                else Claus.advanceWith feed.ClausKinetics feed.ClausMode tRef tauStep comp
            let stateNext = processStateAt feed pNext compNext tNext
            let duty = feed.MassFlow * max 0.0 (stateNow.TotalSpecificEnthalpy - stateNext.TotalSpecificEnthalpy)
            let nIn = totalMolarFlow feed.MassFlow comp
            let nOut = totalMolarFlow feed.MassFlow compNext
            let condensedAtomsIn = stateNow.CondensedAtoms * nIn
            let condensedAtomsOut = stateNext.CondensedAtoms * nOut
            let dCondensedAtoms = max 0.0 (condensedAtomsOut - condensedAtomsIn)
            let dutyLatent = dCondensedAtoms * Sulphur.latentHeatPerAtom tRef
            let dutySensible = max 0.0 (duty - dutyLatent)
            let dt1 = tNow - spec.TCoolant
            let dt2 = tNext - spec.TCoolant
            let lm = lmtdPositive dt1 dt2
            let area = duty / (spec.UAssumed * lm)
            segments.Add
                { Index = i
                  TIn = tNow
                  TOut = tNext
                  PIn = pNow
                  POut = pNext
                  Duty = duty
                  DutyLatent = dutyLatent
                  DutySensible = dutySensible
                  AreaRequired = area
                  YElementalSulphurIn = stateNow.YElementalSulphurVapour
                  YElementalSulphurOut = stateNext.YElementalSulphurVapour
                  CondensedFractionIn = stateNow.CondensedFraction
                  CondensedFractionOut = stateNext.CondensedFraction
                  SulphurDewPointIn = stateNow.SulphurDewPoint
                  SulphurDewPointOut = stateNext.SulphurDewPoint }
            dutyTotal <- dutyTotal + duty
            latentTotal <- latentTotal + dutyLatent
            sensibleTotal <- sensibleTotal + dutySensible
            areaTotal <- areaTotal + area
            comp <- compNext
            tNow <- tNext
            pNow <- pNext
            stateNow <- stateNext
        let outletState = stateNow
        let outletScreen = Sulphur.clausScreening pNow outletState.VapourComposition
        let hasSulphurService =
            inletState.PSulphur > 1e-6
            || outletState.PSulphur > 1e-6
            || Sulphur.hasElementalSulphur feed.Composition
            || Claus.hasReactiveSpecies feed.Composition
        let fog =
            Sulphur.assessFog outletState.T (max inletState.PSulphur outletState.PSulphur) 1.2
                (outletState.T - inletState.T) (outletState.PSulphur - inletState.PSulphur)
        let checks =
            [ if hasSulphurService then
                  yield! Sulphur.condenserChecks spec.TWall outletState.T (max inletState.PSulphur outletState.PSulphur) fog
              else
                  yield
                    { Severity = Sulphur.Watch
                      Title = "Nessuno zolfo elementare disponibile per la condensazione"
                      Value = "p(zolfo) ~ 0 lungo il tratto"
                      Limit = "servizio Claus con zolfo elementare o conversione Claus attiva"
                      Detail = "Il modulo dedicato gira comunque e calcola il solo raffreddamento sensibile, ma la parte di condensazione zolfo non e' rappresentativa su questo feed." }
              yield Sulphur.checkSulphidation spec.TWall outletScreen.YH2S
              match outletScreen.WaterDewPoint with
              | Some tDew -> yield Sulphur.checkWetH2S spec.TWall tDew outletScreen.YH2S
              | None -> () ]
        { SourceLabel = sourceLabel
          SpecUsed = spec
          FeedUsed = feed
          InletState = inletState
          OutletState = outletState
          OutletComposition = comp
          Segments = List.ofSeq segments
          Duty = dutyTotal
          DutyLatent = latentTotal
          DutySensible = sensibleTotal
          AreaRequired = areaTotal
          CondensedSulphurAtomsFlow = outletState.CondensedAtoms * totalMolarFlow feed.MassFlow comp
          CondensedSulphurMassFlow = outletState.CondensedAtoms * totalMolarFlow feed.MassFlow comp * sulphurAtomMolarMass
          SteamPressureForWall = Sulphur.steamPressureForWall spec.TWall
          Fog = fog
          Checks = checks }

    let solve (spec: Spec) =
        solveWithFeed
            (if spec.UseWhbOutlet then "WHB outlet" else "Dedicated sulphur-condenser feed")
            spec spec.Feed
