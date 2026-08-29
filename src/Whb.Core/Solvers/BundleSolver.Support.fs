namespace Whb.Core

open System
open Constants
open Types

module BundleSolverSupport =
    type ProcessModel =
        { Active: bool
          Composition0: GasProps.Composition
          InitialState: Sulphur.ProcessState option
          StateFromEnthalpyAt: float -> GasProps.Composition -> float -> Sulphur.ProcessState }

    type PropertyResolvers =
        { WallMu: float -> float -> float
          TubeK: float -> float }

    let buildProcessModel (case: DesignCase) =
        let comp0 = GasProps.normalize case.Gas.Composition
        let active =
            Sulphur.hasElementalSulphur comp0
            || (case.Gas.ClausMode <> Claus.Frozen && Claus.hasReactiveSpecies comp0)
        let stateFromEnthalpyAt pPa comp h =
            if active then
                Sulphur.processStateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pPa comp h
            else
                let (t0, compGas) = Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pPa comp h
                let st : Sulphur.ProcessState =
                    { T = t0
                      VapourComposition = compGas
                      TotalSpecificEnthalpy = h
                      CpApprox = (GasProps.mixReal GasProps.Wilke case.Gas.RealGas compGas t0 pPa case.Gas.Z).Cp
                      PSulphur = 0.0
                      YElementalSulphurVapour = 0.0
                      SulphurDewPoint = None
                      Condensing = false
                      CondensedAtoms = 0.0
                      CondensedFraction = 0.0 }
                st
        let initialState =
            let st =
                if active then
                    Sulphur.processStateAt case.Gas.ShiftMode case.Gas.RealGas case.Gas.PIn comp0 case.Gas.TIn
                else
                    stateFromEnthalpyAt case.Gas.PIn comp0
                        (GasProps.enthalpyAbsReal case.Gas.RealGas comp0 case.Gas.TIn case.Gas.PIn)
            if active then Some st else None
        { Active = active
          Composition0 = comp0
          InitialState = initialState
          StateFromEnthalpyAt = stateFromEnthalpyAt }

    let buildPropertyResolvers (case: DesignCase) (composition0: GasProps.Composition) =
        let wallMuCache = Collections.Generic.Dictionary<struct (float * float), float>()
        let tubeKCache = Collections.Generic.Dictionary<float, float>()
        let wallMu tK pPa =
            let tRound = Math.Round(tK * 2.0) / 2.0
            let key = struct (tRound, Math.Round(pPa / 100.0) * 100.0)
            match wallMuCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v =
                    (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas composition0
                         tRound pPa case.Gas.Z).Mu
                wallMuCache.[key] <- v
                v
        let tubeK tC =
            let key = Math.Round(tC * 2.0) / 2.0
            match tubeKCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = case.Material.K key
                tubeKCache.[key] <- v
                v
        { WallMu = wallMu; TubeK = tubeK }

    let initialGasEnthalpy (case: DesignCase) (processModel: ProcessModel) =
        match processModel.InitialState with
        | Some st -> st.TotalSpecificEnthalpy
        | None -> GasProps.enthalpyAbsReal case.Gas.RealGas processModel.Composition0 case.Gas.TIn case.Gas.PIn

    let advanceProcessComposition (case: DesignCase) (tRef: float) (tau: float)
                                  (composition: GasProps.Composition) =
        if case.Gas.ClausMode = Claus.Frozen then composition
        else Claus.advanceWith case.Gas.ClausKinetics case.Gas.ClausMode tRef tau composition

    let cellPressureDrop (case: DesignCase) (props: GasProps.MixProps)
                         (gasResult: GasSide.GasHtcResult) (bore: float)
                         (z: float) (dz: float) (ferruleLength: float) (inFerrule: bool)
                         (cellIndex: int) (axialCount: int) =
        let f = GasSide.darcyFriction gasResult.Re (case.Tube.Roughness / bore)
        let dpF = GasSide.dpFrictionPerM f bore props.Rho gasResult.Velocity * dz
        let dpLoc =
            if cellIndex = 0 then GasSide.dpLocal 0.5 props.Rho gasResult.Velocity
            elif inFerrule && (z + dz) >= ferruleLength then
                GasSide.dpLocal ((1.0 - (case.Ferrule.Bore / case.Tube.Di) ** 2.0) ** 2.0) props.Rho gasResult.Velocity
            elif cellIndex = axialCount - 1 then GasSide.dpLocal 1.0 props.Rho gasResult.Velocity
            else 0.0
        (dpF, dpLoc)

    let summarizeAxialSlice (case: DesignCase) (sat: Steam.SatProps) (cells: CellResult[,,])
                            (steamLin: float[]) (dutyLin: float[]) (steamCum: float)
                            (dutyCum: float) (wField: float) (index: int)
                            (bandCount: int) (classCount: int) =
        let mutable wSum = 0.0
        let mutable tGasWeighted = 0.0
        let mutable tGasMin = Double.PositiveInfinity
        let mutable tGasMax = Double.NegativeInfinity
        let mutable qFluxWeighted = 0.0
        let mutable qFluxMax = Double.NegativeInfinity
        let mutable tMetalInMax = Double.NegativeInfinity
        let mutable tMetalOutMax = Double.NegativeInfinity
        let mutable dnbrMin = Double.PositiveInfinity
        let mutable pGasWeighted = 0.0
        for j in 0 .. bandCount - 1 do
            for c in 0 .. classCount - 1 do
                let cell = cells.[index, j, c]
                let wt = cell.NTubes
                wSum <- wSum + wt
                tGasWeighted <- tGasWeighted + cell.TGas * wt
                pGasWeighted <- pGasWeighted + cell.PGas * wt
                qFluxWeighted <- qFluxWeighted + cell.QFluxOut * wt
                if cell.TGas < tGasMin then tGasMin <- cell.TGas
                if cell.TGas > tGasMax then tGasMax <- cell.TGas
                if cell.QFluxOut > qFluxMax then qFluxMax <- cell.QFluxOut
                if cell.TMetalIn > tMetalInMax then tMetalInMax <- cell.TMetalIn
                if cell.TMetalOut > tMetalOutMax then tMetalOutMax <- cell.TMetalOut
                if cell.DNBR < dnbrMin then dnbrMin <- cell.DNBR
        let top = cells.[index, bandCount - 1, 0]
        let bot = cells.[index, 0, 0]
        let alphaTop = TwoPhase.voidFraction case.Loop.VoidModel top.XOut sat top.GCross
        let rhoHTop = TwoPhase.homogeneousDensity top.XOut sat
        let invWSum = 1.0 / max 1e-12 wSum
        { Z = top.Z
          TGasMean = tGasWeighted * invWSum
          TGasMin = tGasMin
          TGasMax = tGasMax
          QFluxMean = qFluxWeighted * invWSum
          QFluxMax = qFluxMax
          TMetalInMax = tMetalInMax
          TMetalOutMax = tMetalOutMax
          SteamLin = steamLin.[index]
          DutyLin = dutyLin.[index]
          WFieldLin = wField
          WBypassLin = 0.0
          XTop = top.XOut
          AlphaTop = alphaTop
          GCross = top.GCross
          VelLiqIn = bot.GCross / sat.RhoL
          VelMixOut = top.GCross / rhoHTop
          VelVapOut = (if alphaTop > 1e-6 then top.GCross * top.XOut / (sat.RhoV * alphaTop) else 0.0)
          VelAxialBottom = 0.0
          VelAxialTop = 0.0
          DNBRMin = dnbrMin
          PGas = pGasWeighted * invWSum
          SteamCum = steamCum
          DutyCum = dutyCum }

    let outletStates (processModel: ProcessModel) (pGas: float[,]) (procComp: GasProps.Composition[,])
                     (hGas: float[,]) (bandCount: int) (classCount: int) =
        if processModel.Active then
            Some(Array2D.init bandCount classCount (fun j c ->
                processModel.StateFromEnthalpyAt pGas.[j, c] procComp.[j, c] hGas.[j, c]))
        else None

    let outletTemperatures (case: DesignCase) (outletStates: Sulphur.ProcessState[,] option)
                           (pGas: float[,]) (procComp: GasProps.Composition[,]) (hGas: float[,])
                           (bandCount: int) (classCount: int) =
        match outletStates with
        | Some states -> Array2D.init bandCount classCount (fun j c -> states.[j, c].T)
        | None ->
            Array2D.init bandCount classCount (fun j c ->
                fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] procComp.[j, c] hGas.[j, c]))

    let momentumPressureDrop (case: DesignCase) (processModel: ProcessModel)
                             (outletStates: Sulphur.ProcessState[,] option)
                             (pGas: float[,]) (tOut: float[,]) (procComp: GasProps.Composition[,])
                             (mdotPerTube: float) (bandCount: int) (classCount: int) =
        let aTube = Math.PI * case.Tube.Di * case.Tube.Di / 4.0
        let gTube = mdotPerTube / aTube
        let rhoIn =
            (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas processModel.Composition0 case.Gas.TIn case.Gas.PIn case.Gas.Z).Rho
        Array2D.init bandCount classCount (fun j c ->
            let rhoOut =
                (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas
                    (match outletStates with
                     | Some states -> states.[j, c].VapourComposition
                     | None -> procComp.[j, c])
                    tOut.[j, c] pGas.[j, c] case.Gas.Z).Rho
            gTube * gTube * (1.0 / rhoOut - 1.0 / rhoIn))

    let weightedGasPressureDrop (case: DesignCase) (bandArr: Bundle.Band[]) (classArr: (float * float)[])
                                (pGas: float[,]) (dpMomentum: float[,]) (bandCount: int) (classCount: int) =
        let mutable dpAcc = 0.0
        let mutable dpWeight = 0.0
        for j in 0 .. bandCount - 1 do
            for c in 0 .. classCount - 1 do
                let w = bandArr.[j].NTubes * fst classArr.[c]
                dpAcc <- dpAcc + w * (case.Gas.PIn - pGas.[j, c] + dpMomentum.[j, c])
                dpWeight <- dpWeight + w
        dpAcc / max 1e-12 dpWeight

    let sulphurSummary (outletStates: Sulphur.ProcessState[,] option)
                       (initialState: Sulphur.ProcessState option) (bandArr: Bundle.Band[])
                       (classArr: (float * float)[]) (bandCount: int) (classCount: int)
                       (sulphurCondensingCells: int) (sulphurFirstCondensationZ: float) =
        match outletStates, initialState with
        | Some states, Some inlet ->
            let mutable yAcc = 0.0
            let mutable cfAcc = 0.0
            let mutable wtAcc = 0.0
            for j in 0 .. bandCount - 1 do
                for c in 0 .. classCount - 1 do
                    let w = bandArr.[j].NTubes * fst classArr.[c]
                    let st = states.[j, c]
                    yAcc <- yAcc + w * st.YElementalSulphurVapour
                    cfAcc <- cfAcc + w * st.CondensedFraction
                    wtAcc <- wtAcc + w
            let summary : Sulphur.CouplingSummary =
                { InletElementalSulphurVapour = inlet.YElementalSulphurVapour
                  InletSulphurDewPoint = inlet.SulphurDewPoint
                  CondensingCells = sulphurCondensingCells
                  FirstCondensationZ = sulphurFirstCondensationZ
                  OutletCondensedFraction = cfAcc / max 1e-12 wtAcc
                  OutletElementalSulphurVapour = yAcc / max 1e-12 wtAcc }
            Some summary
        | _ -> None
