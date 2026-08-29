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

    [<Struct>]
    type private PreparedSolve =
        { SourceLabel: string
          Spec: Spec
          Feed: Feed
          TOutTarget: float
          DtTotal: float
          DpStep: float
          TauStep: float
          InletState: Sulphur.ProcessState }

    [<Struct>]
    type private MarchState =
        { Composition: GasProps.Composition
          TNow: float
          PNow: float
          StateNow: Sulphur.ProcessState }

    [<Struct>]
    type private SegmentStep =
        { Segment: Segment
          Next: MarchState
          Duty: float
          DutyLatent: float
          DutySensible: float
          AreaRequired: float }

    [<Struct>]
    type private Totals =
        { Duty: float
          DutyLatent: float
          DutySensible: float
          AreaRequired: float }

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

    let private prepareSolve (sourceLabel: string) (specIn: Spec) (feedIn: Feed) =
        let spec = sanitizeSpec { specIn with Feed = feedIn }
        let feed = sanitizeFeed feedIn
        let inletState =
            processStateAt feed feed.PIn (GasProps.normalize feed.Composition) feed.TIn
        let tOutTarget = min feed.TIn spec.TOutTarget
        { SourceLabel = sourceLabel
          Spec = spec
          Feed = feed
          TOutTarget = tOutTarget
          DtTotal = feed.TIn - tOutTarget
          DpStep = spec.DpTotal / float spec.Sections
          TauStep = spec.ResidenceTime / float spec.Sections
          InletState = inletState }

    let private initialMarchState (prepared: PreparedSolve) =
        { Composition = GasProps.normalize prepared.Feed.Composition
          TNow = prepared.Feed.TIn
          PNow = prepared.Feed.PIn
          StateNow = prepared.InletState }

    let private advanceComposition (feed: Feed) (tRef: float) (tauStep: float) (composition: GasProps.Composition) =
        if feed.ClausMode = Claus.Frozen then composition
        else Claus.advanceWith feed.ClausKinetics feed.ClausMode tRef tauStep composition

    let private latentDuty (feed: Feed) (composition: GasProps.Composition) (nextComposition: GasProps.Composition)
                           (stateNow: Sulphur.ProcessState) (stateNext: Sulphur.ProcessState) (tRef: float) =
        let nIn = totalMolarFlow feed.MassFlow composition
        let nOut = totalMolarFlow feed.MassFlow nextComposition
        let condensedAtomsIn = stateNow.CondensedAtoms * nIn
        let condensedAtomsOut = stateNext.CondensedAtoms * nOut
        max 0.0 (condensedAtomsOut - condensedAtomsIn) * Sulphur.latentHeatPerAtom tRef

    let private requiredArea (spec: Spec) (tIn: float) (tOut: float) (duty: float) =
        let lm = lmtdPositive (tIn - spec.TCoolant) (tOut - spec.TCoolant)
        duty / (spec.UAssumed * lm)

    let private buildSegment (index: int) (state: MarchState) (stateNext: Sulphur.ProcessState)
                             (pNext: float) (tNext: float) (duty: float) (dutyLatent: float)
                             (area: float) =
        { Index = index
          TIn = state.TNow
          TOut = tNext
          PIn = state.PNow
          POut = pNext
          Duty = duty
          DutyLatent = dutyLatent
          DutySensible = max 0.0 (duty - dutyLatent)
          AreaRequired = area
          YElementalSulphurIn = state.StateNow.YElementalSulphurVapour
          YElementalSulphurOut = stateNext.YElementalSulphurVapour
          CondensedFractionIn = state.StateNow.CondensedFraction
          CondensedFractionOut = stateNext.CondensedFraction
          SulphurDewPointIn = state.StateNow.SulphurDewPoint
          SulphurDewPointOut = stateNext.SulphurDewPoint }

    let private stepSegment (prepared: PreparedSolve) (state: MarchState) (index: int) =
        let frac = float index / float prepared.Spec.Sections
        let tNext = prepared.Feed.TIn - frac * prepared.DtTotal
        let pNext = max 1.0e4 (prepared.Feed.PIn - float index * prepared.DpStep)
        let tRef = 0.5 * (state.TNow + tNext)
        let nextComposition = advanceComposition prepared.Feed tRef prepared.TauStep state.Composition
        let stateNext = processStateAt prepared.Feed pNext nextComposition tNext
        let duty =
            prepared.Feed.MassFlow
            * max 0.0 (state.StateNow.TotalSpecificEnthalpy - stateNext.TotalSpecificEnthalpy)
        let dutyLatent =
            latentDuty prepared.Feed state.Composition nextComposition state.StateNow stateNext tRef
        let area = requiredArea prepared.Spec state.TNow tNext duty
        let segment = buildSegment index state stateNext pNext tNext duty dutyLatent area
        { Segment = segment
          Next =
            { Composition = nextComposition
              TNow = tNext
              PNow = pNext
              StateNow = stateNext }
          Duty = duty
          DutyLatent = dutyLatent
          DutySensible = segment.DutySensible
          AreaRequired = area }

    let private addTotals (totals: Totals) (step: SegmentStep) =
        { Duty = totals.Duty + step.Duty
          DutyLatent = totals.DutyLatent + step.DutyLatent
          DutySensible = totals.DutySensible + step.DutySensible
          AreaRequired = totals.AreaRequired + step.AreaRequired }

    let private marchSegments (prepared: PreparedSolve) =
        let folder (segments, totals, state) index =
            let step = stepSegment prepared state index
            (step.Segment :: segments, addTotals totals step, step.Next)

        [ 1 .. prepared.Spec.Sections ]
        |> List.fold
            folder
            ([],
             { Duty = 0.0; DutyLatent = 0.0; DutySensible = 0.0; AreaRequired = 0.0 },
             initialMarchState prepared)
        |> fun (segmentsRev, totals, state) -> (List.rev segmentsRev, totals, state)

    let private hasSulphurService (feed: Feed) (inletState: Sulphur.ProcessState) (outletState: Sulphur.ProcessState) =
        inletState.PSulphur > 1e-6
        || outletState.PSulphur > 1e-6
        || Sulphur.hasElementalSulphur feed.Composition
        || Claus.hasReactiveSpecies feed.Composition

    let private assessFog (inletState: Sulphur.ProcessState) (outletState: Sulphur.ProcessState) =
        Sulphur.assessFog outletState.T (max inletState.PSulphur outletState.PSulphur) 1.2
            (outletState.T - inletState.T) (outletState.PSulphur - inletState.PSulphur)

    let private buildChecks (spec: Spec) (feed: Feed) (inletState: Sulphur.ProcessState)
                            (outletPressure: float) (outletState: Sulphur.ProcessState) (fog: Sulphur.FogAssessment) =
        let outletScreen = Sulphur.clausScreening outletPressure outletState.VapourComposition
        [ if hasSulphurService feed inletState outletState then
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

    let private assembleResult (prepared: PreparedSolve) (segments: Segment list) (totals: Totals)
                               (finalState: MarchState) =
        let outletState = finalState.StateNow
        let fog = assessFog prepared.InletState outletState
        let checks = buildChecks prepared.Spec prepared.Feed prepared.InletState finalState.PNow outletState fog
        let outletMolarFlow = totalMolarFlow prepared.Feed.MassFlow finalState.Composition
        { SourceLabel = prepared.SourceLabel
          SpecUsed = prepared.Spec
          FeedUsed = prepared.Feed
          InletState = prepared.InletState
          OutletState = outletState
          OutletComposition = finalState.Composition
          Segments = segments
          Duty = totals.Duty
          DutyLatent = totals.DutyLatent
          DutySensible = totals.DutySensible
          AreaRequired = totals.AreaRequired
          CondensedSulphurAtomsFlow = outletState.CondensedAtoms * outletMolarFlow
          CondensedSulphurMassFlow = outletState.CondensedAtoms * outletMolarFlow * sulphurAtomMolarMass
          SteamPressureForWall = Sulphur.steamPressureForWall prepared.Spec.TWall
          Fog = fog
          Checks = checks }

    let solveWithFeed (sourceLabel: string) (specIn: Spec) (feedIn: Feed) =
        let prepared = prepareSolve sourceLabel specIn feedIn
        let (segments, totals, finalState) = marchSegments prepared
        assembleResult prepared segments totals finalState

    let solve (spec: Spec) =
        solveWithFeed
            (if spec.UseWhbOutlet then "WHB outlet" else "Dedicated sulphur-condenser feed")
            spec spec.Feed
