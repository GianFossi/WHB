namespace Whb.Core

open System
open Constants
open Types

module Sizing =
    type Targets =
        { Duty: float
          Steam: float
          TGasOut: float
          MaxDpGas: float
          MaxQFlux: float
          MinDNBR: float
          MinCirculationRatio: float
          MaxFeiRatio: float
          NozzleHeight: float
          DrumWallThickness: float
          PipingWallThickness: float
          DutyTolerance: float
          SteamTolerance: float
          TGasOutTolerance: float }

    type Check =
        { Name: string
          Current: string
          Target: string
          Ok: bool
          Note: string }

    type Action =
        { Area: string
          Current: string
          Required: string
          Benefit: string
          WeightLengthImpact: string
          FeasibleOnExisting: bool
          Note: string }

    type GeometryObjective =
        { DrumCenterlineHeight: float
          MinimumDrumCenterlineHeight: float
          NozzleHeight: float
          MinimumRiserSpool: float
          WhbOuterDiameter: float
          DrumOuterDiameter: float
          WhbIdLength: float
          DrumIdLength: float
          RiserWeightKg: float
          DowncomerWeightKg: float
          RiserDevelopedLength: float
          DowncomerDevelopedLength: float
          Note: string }

    type Summary =
        { Targets: Targets
          Checks: Check list
          Actions: Action list
          Geometry: GeometryObjective
          WeightEstimateKg: float
          TubeLength: float
          ObjectiveNote: string }

    let defaultTargets (r: DesignResult) =
        let c = r.Case
        { Duty = r.Duty
          Steam = r.SteamProduction
          TGasOut = if r.Case.Bypass.Enabled then r.Case.Bypass.TargetMixOut else r.TGasOutMean
          MaxDpGas = 30000.0
          MaxQFlux = 300000.0
          MinDNBR = c.Water.MinDNBR
          MinCirculationRatio = 10.0
          MaxFeiRatio = 0.8
          NozzleHeight = 0.200
          DrumWallThickness = c.ShellThickness
          PipingWallThickness = c.ShellThickness
          DutyTolerance = 0.005
          SteamTolerance = 0.005
          TGasOutTolerance = 2.0 }

    let private fmt0 (x: float) = x.ToString("F0", Globalization.CultureInfo.InvariantCulture)
    let private fmt1 (x: float) = x.ToString("F1", Globalization.CultureInfo.InvariantCulture)
    let private fmt2 (x: float) = x.ToString("F2", Globalization.CultureInfo.InvariantCulture)
    let private materialDensity (mat: Materials.Material) =
        let name = mat.Name.ToUpperInvariant()
        if name.Contains("ALLOY") then 8050.0
        elif name.Contains("AUSTENITICO") || name.Contains("321") then 8000.0
        else 7850.0

    let private estimateWeight (c: DesignCase) =
        let rhoTube = materialDensity c.Material
        let rhoShell = materialDensity c.ShellMaterial
        let tubeMetal =
            Math.PI / 4.0 * (c.Tube.Do * c.Tube.Do - c.Tube.Di * c.Tube.Di) * c.Tube.Length * float c.Tube.NTubes * rhoTube
        let shellMetal =
            Math.PI / 4.0 * ((c.Tube.ShellId + 2.0 * c.ShellThickness) ** 2.0 - c.Tube.ShellId ** 2.0) * c.Tube.Length * rhoShell
        let baffleMetal =
            let count = max 0 (List.length c.BaffleSpans - 1)
            let gross = Math.PI / 4.0 * c.Tube.BaffleOd * c.Tube.BaffleOd
            let holes = Math.PI / 4.0 * c.Tube.Do * c.Tube.Do * float c.Tube.NTubes
            max 0.0 (gross - holes) * c.BaffleThickness * float count * rhoShell
        tubeMetal + shellMetal + baffleMetal

    let private lineMetalWeight density wall (l: Piping.Line) =
        let id = max 0.001 l.Id
        let od = id + 2.0 * max 0.0 wall
        let area = Math.PI / 4.0 * (od * od - id * id)
        area * Piping.developedLength l * float l.Count * density

    let private spoolMinimum (targets: Targets) (lines: Piping.Line list) =
        let odOf (l: Piping.Line) = l.Id + 2.0 * max 0.0 targets.PipingWallThickness
        match lines with
        | [] -> 0.500
        | _ ->
            lines
            |> List.map (fun l -> min 0.500 (3.0 * odOf l))
            |> List.max

    let private geometryObjective (targets: Targets) (c: DesignCase) =
        let whbOd = c.Tube.ShellId + 2.0 * c.ShellThickness
        let drumId, drumLength =
            if c.Loop.Drum.Enabled then
                c.Loop.Drum.ShellId, c.Loop.Drum.Length
            else
                0.0, 0.0
        let drumOd =
            if c.Loop.Drum.Enabled then drumId + 2.0 * targets.DrumWallThickness else 0.0
        let risers = c.Loop.Risers |> List.filter (fun l -> l.Connected)
        let downcomers = c.Loop.Downcomers |> List.filter (fun l -> l.Connected)
        let minSpool = spoolMinimum targets risers
        let minCenterline =
            if c.Loop.Drum.Enabled then
                0.5 * whbOd + 0.5 * drumOd + targets.NozzleHeight + minSpool
            else
                c.Loop.DzDrumWhb
        let rho = materialDensity c.ShellMaterial
        let weightOf lines = lines |> List.sumBy (lineMetalWeight rho targets.PipingWallThickness)
        let lengthOf lines = lines |> List.sumBy (fun l -> Piping.developedLength l * float l.Count)
        { DrumCenterlineHeight = c.Loop.DzDrumWhb
          MinimumDrumCenterlineHeight = minCenterline
          NozzleHeight = targets.NozzleHeight
          MinimumRiserSpool = minSpool
          WhbOuterDiameter = whbOd
          DrumOuterDiameter = drumOd
          WhbIdLength = c.Tube.ShellId * c.Tube.Length
          DrumIdLength = drumId * drumLength
          RiserWeightKg = weightOf risers
          DowncomerWeightKg = weightOf downcomers
          RiserDevelopedLength = lengthOf risers
          DowncomerDevelopedLength = lengthOf downcomers
          Note = "Minimize the steam-drum centerline elevation against the WHB centerline after allowing WHB OD, drum OD, nozzle height, and minimum riser spool. Then minimize external piping weight and the ID x L envelopes of WHB and steam drum." }

    let private baffleCount length thk maxSpan =
        let maxSpan = max 0.10 maxSpan
        let n = max 0 (int (ceil ((length - maxSpan) / (maxSpan + thk))))
        let span = (length - float n * thk) / float (n + 1)
        n, span

    let private ferruleLengthFor target (axial: AxialResult list) =
        match axial with
        | [] -> None
        | _ ->
            let zPeak = (axial |> List.maxBy (fun a -> a.QFluxMax)).Z
            axial
            |> List.filter (fun a -> a.Z >= zPeak && a.QFluxMax <= target)
            |> function
               | [] -> None
               | xs -> Some((xs |> List.minBy (fun a -> a.Z)).Z)

    let evaluate (targets: Targets) (r: DesignResult) =
        let c = r.Case
        let hot = r.Cells |> List.filter (fun x -> not x.InFerrule)
        let qMax = hot |> List.maxBy (fun x -> x.QFluxOut) |> fun x -> x.QFluxOut
        let dnbrMin = hot |> List.minBy (fun x -> x.DNBR) |> fun x -> x.DNBR
        let feiMax = if r.Vibration.IsEmpty then 0.0 else r.Vibration |> List.maxBy (fun v -> v.FeiRatio) |> fun v -> v.FeiRatio
        let weight = estimateWeight c
        let geom = geometryObjective targets c

        let relOk value target tol = abs (value / max 1e-9 target - 1.0) <= tol
        let checks =
            [ { Name = "PDS: thermal duty"
                Current = sprintf "%s MW" (fmt2 (r.Duty / 1e6))
                Target = sprintf "%s MW +/- %s%%" (fmt2 (targets.Duty / 1e6)) (fmt1 (100.0 * targets.DutyTolerance))
                Ok = relOk r.Duty targets.Duty targets.DutyTolerance
                Note = "Must match the process heat balance." }
              { Name = "PDS: generated steam"
                Current = sprintf "%s t/h" (fmt1 (r.SteamProduction * 3.6))
                Target = sprintf "%s t/h +/- %s%%" (fmt1 (targets.Steam * 3.6)) (fmt1 (100.0 * targets.SteamTolerance))
                Ok = relOk r.SteamProduction targets.Steam targets.SteamTolerance
                Note = "Steam production follows duty and drum pressure." }
              { Name = "PDS: gas outlet temperature"
                Current = sprintf "%s degC" (fmt1 (kToC r.TGasOutMean))
                Target = sprintf "%s degC +/- %s K" (fmt1 (kToC targets.TGasOut)) (fmt1 targets.TGasOutTolerance)
                Ok = abs (r.TGasOutMean - targets.TGasOut) <= targets.TGasOutTolerance
                Note = "For bypass cases this is the mixed outlet temperature target." }
              { Name = "PDS: gas-side pressure drop"
                Current = sprintf "%s mbar" (fmt0 (r.DpGas / 100.0))
                Target = sprintf "<= %s mbar" (fmt0 (targets.MaxDpGas / 100.0))
                Ok = r.DpGas <= targets.MaxDpGas
                Note = "Pressure drop is usually the active sizing constraint for fire-tube WHBs." }
              { Name = "Peak heat flux"
                Current = sprintf "%s kW/m2" (fmt0 (qMax / 1000.0))
                Target = sprintf "<= %s kW/m2" (fmt0 (targets.MaxQFlux / 1000.0))
                Ok = qMax <= targets.MaxQFlux
                Note = "Default limit is 300 kW/m2." }
              { Name = "DNBR"
                Current = fmt2 dnbrMin
                Target = sprintf ">= %s" (fmt2 targets.MinDNBR)
                Ok = dnbrMin >= targets.MinDNBR
                Note = "Default minimum is 2.0." }
              { Name = "Circulation ratio"
                Current = fmt1 r.Circulation.CirculationRatio
                Target = sprintf ">= %s" (fmt1 targets.MinCirculationRatio)
                Ok = r.Circulation.CirculationRatio >= targets.MinCirculationRatio
                Note = "Default minimum is 10." }
              { Name = "Vibration FEI"
                Current = fmt2 feiMax
                Target = sprintf "<= %s" (fmt2 targets.MaxFeiRatio)
                Ok = feiMax <= targets.MaxFeiRatio
                Note = "Default screening limit is V/Vcrit <= 0.8." } ]
            @ [ { Name = "Drum centerline elevation"
                  Current = sprintf "%s m" (fmt2 geom.DrumCenterlineHeight)
                  Target = sprintf "minimize, >= %s m" (fmt2 geom.MinimumDrumCenterlineHeight)
                  Ok = geom.DrumCenterlineHeight >= geom.MinimumDrumCenterlineHeight
                  Note = "Lower steam-drum centerline reduces riser/downcomer length and weight, but must keep WHB OD, drum OD, nozzles, and riser spool clearance." }
                { Name = "Minimum riser spool"
                  Current = sprintf "nozzle %s mm" (fmt0 (1000.0 * geom.NozzleHeight))
                  Target = sprintf "spool >= %s mm" (fmt0 (1000.0 * geom.MinimumRiserSpool))
                  Ok = true
                  Note = "Default nozzle height is 200 mm; automatic nozzle sizing can replace this later." }
                { Name = "WHB ID x L"
                  Current = sprintf "%s m2" (fmt2 geom.WhbIdLength)
                  Target = "minimize after PDS and margins"
                  Ok = true
                  Note = "Compact WHB envelope objective: shell ID multiplied by tube length." }
                { Name = "Steam drum ID x L"
                  Current = sprintf "%s m2" (fmt2 geom.DrumIdLength)
                  Target = "minimize after separation/circulation checks"
                  Ok = true
                  Note = "Compact drum envelope objective: drum ID multiplied by drum tangent length." } ]

        let actions = ResizeArray<Action>()

        if r.DpGas > targets.MaxDpGas then
            let factor = sqrt (r.DpGas / targets.MaxDpGas)
            actions.Add
                { Area = "Gas pressure drop"
                  Current = sprintf "%s mbar" (fmt0 (r.DpGas / 100.0))
                  Required = sprintf "Increase total tube flow area by about %s%%, or accept a larger tube count." (fmt0 ((factor - 1.0) * 100.0))
                  Benefit = "Meets the PDS gas-side pressure-drop limit."
                  WeightLengthImpact = "More tubes increase bundle diameter and weight, but can avoid increasing tube length."
                  FeasibleOnExisting = false
                  Note = "For a new unit, tube count is the cleanest lever. For an existing unit this is usually a redesign item." }

        if qMax > targets.MaxQFlux || dnbrMin < targets.MinDNBR then
            let areaFactor = max (qMax / targets.MaxQFlux) (targets.MinDNBR / max 1e-9 dnbrMin)
            let ferrule =
                match ferruleLengthFor targets.MaxQFlux r.Axial with
                | Some z -> sprintf "extend protected inlet length to about %s mm" (fmt0 (1000.0 * z))
                | None -> "increase heat-transfer area or reduce local heat flux; axial profile never falls below the heat-flux target"
            actions.Add
                { Area = "Heat flux / DNBR"
                  Current = sprintf "q''max %s kW/m2, DNBR %s" (fmt0 (qMax / 1000.0)) (fmt2 dnbrMin)
                  Required = sprintf "Area factor about %s, or %s." (fmt2 areaFactor) ferrule
                  Benefit = "Keeps peak flux below the limit and raises DNBR."
                  WeightLengthImpact = "Ferrules add little weight and no tube length; more area adds weight or length."
                  FeasibleOnExisting = true
                  Note = "The weight/length objective prefers ferrule/protection changes first, then extra tubes, and only last additional tube length." }

        if r.Circulation.CirculationRatio < targets.MinCirculationRatio then
            let factor = targets.MinCirculationRatio / max 1e-9 r.Circulation.CirculationRatio
            actions.Add
                { Area = "Natural circulation"
                  Current = sprintf "CR %s" (fmt1 r.Circulation.CirculationRatio)
                  Required = sprintf "Increase riser/downcomer area by about %s%% or raise drum elevation toward %s m." (fmt0 ((factor - 1.0) * 100.0)) (fmt2 (c.Loop.DzDrumWhb * factor * factor))
                  Benefit = "Raises circulation ratio above the target."
                  WeightLengthImpact = "Larger external piping adds less WHB bundle weight than adding tube length."
                  FeasibleOnExisting = true
                  Note = "This is a first-order hydraulic inversion; final values must be checked with the full circulation solve." }

        if geom.DrumCenterlineHeight > geom.MinimumDrumCenterlineHeight * 1.02 then
            actions.Add
                { Area = "Steam-drum elevation"
                  Current = sprintf "centerline difference %s m; minimum screened value %s m" (fmt2 geom.DrumCenterlineHeight) (fmt2 geom.MinimumDrumCenterlineHeight)
                  Required = sprintf "Lower the steam drum toward %s m while rechecking circulation, nozzle geometry, and riser spool clearance." (fmt2 geom.MinimumDrumCenterlineHeight)
                  Benefit = "Reduces riser/downcomer developed length, piping weight, support steel, and static elevation."
                  WeightLengthImpact = sprintf "Current riser/downcomer metal estimate is %s t over %s m developed length." (fmt2 ((geom.RiserWeightKg + geom.DowncomerWeightKg) / 1000.0)) (fmt1 (geom.RiserDevelopedLength + geom.DowncomerDevelopedLength))
                  FeasibleOnExisting = false
                  Note = "The lower bound includes WHB OD, steam-drum OD, default 200 mm nozzle height, and the minimum riser spool criterion." }

        actions.Add
            { Area = "Envelope compactness"
              Current = sprintf "WHB ID x L %s m2; drum ID x L %s m2" (fmt2 geom.WhbIdLength) (fmt2 geom.DrumIdLength)
              Required = "Minimize both products only after PDS duty, generated steam, gas outlet temperature, gas dP, DNBR, heat flux, circulation, and vibration checks remain satisfied."
              Benefit = "Keeps WHB and steam-drum envelopes compact."
              WeightLengthImpact = "Smaller ID x L generally lowers metal weight, external piping length, insulation, platforms, and plot impact."
              FeasibleOnExisting = false
              Note = "This is an optimization objective, not a standalone pass/fail constraint." }

        if feiMax > targets.MaxFeiRatio && not r.Vibration.IsEmpty then
            let gov = r.Vibration |> List.maxBy (fun v -> v.FeiRatio)
            let span = Vibration.maxSpan targets.MaxFeiRatio gov
            let n, pitch = baffleCount c.Tube.Length c.BaffleThickness span
            actions.Add
                { Area = "Vibration"
                  Current = sprintf "V/Vcrit %s at span %s mm" (fmt2 gov.FeiRatio) (fmt0 (1000.0 * gov.Span))
                  Required = sprintf "%d baffles, uniform span about %s mm" n (fmt0 (1000.0 * pitch))
                  Benefit = "Avoids fluid-elastic instability problems."
                  WeightLengthImpact = "Adds baffle weight but does not increase tube length."
                  FeasibleOnExisting = false
                  Note = "For an existing bundle this is usually not practical; for a new unit it is the preferred fix." }

        { Targets = targets
          Checks = checks
          Actions = List.ofSeq actions
          Geometry = geom
          WeightEstimateKg = weight
          TubeLength = c.Tube.Length
          ObjectiveNote = "Objective hierarchy: satisfy PDS first, then safety margins, then minimize WHB metal weight, tube length, steam-drum elevation, riser/downcomer weight, and WHB/drum ID x L envelopes. Preferred levers are low-weight/no-length changes before larger bundle geometry." }
