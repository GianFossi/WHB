namespace Whb.Core

open Constants
open Types

module DesignVibration =

    let run (case: DesignCase) (sat: Steam.SatProps) (t: TubeGeometry) (ny: int)
            (cells: CellResult list) (gasDensityAt: CellResult -> float) =
        let spanRanges =
            if List.isEmpty case.BaffleSpans then
                [ (0.0, t.Length, case.UnsupportedSpan) ]
            else
                let mutable z = 0.0
                [ for sp in case.BaffleSpans ->
                    let z0 = z
                    let z1 = z + sp
                    z <- z1 + case.BaffleThickness
                    (z0, z1, sp) ]
        let nSpans = List.length spanRanges
        let vibrationBest = Array.zeroCreate<Vibration.Result option> ny
        for (si, (z0, z1, sp)) in List.indexed spanRanges do
            let clamped =
                let atTubesheet = (si = 0) || (si = nSpans - 1)
                if atTubesheet && case.TubesheetJoint = Vibration.FullPenetrationWeld then 1 else 0
            let lam = Vibration.lambda2Of clamped
            for j in 0 .. ny - 1 do
                let mutable worst = Unchecked.defaultof<CellResult>
                let mutable hasWorst = false
                for c in cells do
                    if c.J = j && c.Z >= z0 && c.Z <= z1 && (not hasWorst || c.VelCross > worst.VelCross) then
                        worst <- c
                        hasWorst <- true
                if hasWorst then
                    let w = worst
                    let rhoH = TwoPhase.homogeneousDensity w.XOut sat
                    let rhoGas = gasDensityAt w
                    let v =
                        Vibration.check j w.Y sp lam case.TubeLayout case.VibrationDamping
                            t.Do t.Di t.Pitch (case.Material.E (kToC w.TMetalWallAvg)) 7850.0
                            w.VelCross rhoH rhoGas w.Alpha
                    match vibrationBest.[j] with
                    | Some old when old.FeiRatio >= v.FeiRatio -> ()
                    | _ -> vibrationBest.[j] <- Some v
        [ for j in 0 .. ny - 1 ->
            match vibrationBest.[j] with
            | Some v -> v
            | None -> failwithf "No vibration cell found for band %d" j ]
