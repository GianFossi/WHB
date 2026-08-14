namespace Whb.Core

open System
module Bundle =
    type Band =
        { Index: int
          Y: float
          Height: float
          NTubes: float
          ShellWidth: float
          TubedWidth: float
          FieldFreeArea: float
          BypassArea: float
          Rows: float }
    let build (shellId: float) (otl: float) (itl: float) (pitch: float)
              (dOut: float) (nTubes: int) (nBands: int) (bypassOd: float) =
        let rs = shellId / 2.0
        let ro = otl / 2.0
        let ri = itl / 2.0
        let n = max 3 nBands
        let h = otl / float n
        let chord (r: float) (y: float) =
            let a = r * r - y * y
            if a <= 0.0 then 0.0 else 2.0 * sqrt a
        let vPitch = pitch * 0.8660254
        let areaPerTube = pitch * pitch * 0.8660254

        let raw =
            [ for j in 0 .. n - 1 ->
                let y = -ro + h * (float j + 0.5)
                let m = 5
                let mutable tw = 0.0
                let mutable sw = 0.0
                for k in 0 .. m - 1 do
                    let yy = -ro + h * (float j + (float k + 0.5) / float m)
                    tw <- tw + (chord ro yy - chord ri yy) / float m
                    sw <- sw + chord rs yy / float m
                (j, y, tw, sw) ]

        let areaTubed = raw |> List.sumBy (fun (_, _, tw, _) -> tw * h)
        let scale = if areaTubed > 0.0 then float nTubes * areaPerTube / areaTubed else 1.0

        raw
        |> List.map (fun (j, y, tw, sw) ->
            let nt = tw * h * scale / areaPerTube
            let blocked = tw * dOut / pitch
            { Index = j
              Y = y
              Height = h
              NTubes = nt
              ShellWidth = sw
              TubedWidth = tw
              FieldFreeArea = max 1e-6 (tw - blocked)
              BypassArea =
                let blockedByPipe =
                    if bypassOd > 0.0 && abs y < bypassOd / 2.0 then
                        2.0 * sqrt (max 0.0 (bypassOd * bypassOd / 4.0 - y * y))
                    else 0.0
                max 1e-6 (sw - tw - blockedByPipe)
              Rows = h / vPitch })
    let meanBypassArea (bands: Band list) =
        if bands.IsEmpty then 0.0
        else bands |> List.averageBy (fun b -> b.BypassArea)
    let openAnnulusArea (shellId: float) (baffleOd: float) (otl: float) =
        let rs = shellId / 2.0
        let rb = baffleOd / 2.0
        let ro = otl / 2.0
        if rb >= rs then 0.0
        else
            let n = 40
            let chord (r: float) (y: float) =
                let a = r * r - y * y
                if a <= 0.0 then 0.0 else 2.0 * sqrt a
            let mutable acc = 0.0
            for k in 0 .. n - 1 do
                let y = -ro + otl * (float k + 0.5) / float n
                acc <- acc + (chord rs y - chord rb y)
            max 0.0 (acc / float n)
    let totalTubes (bands: Band list) = bands |> List.sumBy (fun b -> b.NTubes)


