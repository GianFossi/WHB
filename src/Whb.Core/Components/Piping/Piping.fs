namespace Whb.Core

open System
open Constants
module Piping =
    type Elbow =
        { AngleDeg: float
          ROverD: float
          Count: int }
    type Line =
        { /// Nozzle tag (R1, DC1, ...)
          Tag: string
          Nps: string
          Id: float
          Count: int
          Straights: float list
          Elbows: Elbow list
          ExtraK: float
          ZNozzle: float
          AngleDeg: float
          Connected: bool
          Note: string }
    let line tag nps idMm count straights elbows extraK z ang note =
        { Tag = tag; Nps = nps; Id = idMm / 1000.0; Count = count
          Straights = straights; Elbows = elbows; ExtraK = extraK
          ZNozzle = z; AngleDeg = ang; Connected = true; Note = note }
    let blind (l: Line) (why: string) =
        { l with Connected = false
                 Note = (if l.Note = "" then why else l.Note + " - " + why) }
    let elbow ang rod n = { AngleDeg = ang; ROverD = rod; Count = n }
    let elbowArc (d: float) (e: Elbow) =
        float e.Count * Math.PI * e.AngleDeg / 180.0 * e.ROverD * d
    let elbowK (f: float) (e: Elbow) =
        let th = e.AngleDeg
        let a1 =
            if th < 70.0 then 0.9 * sin (th * Math.PI / 180.0)
            elif th <= 100.0 then 1.0
            else 0.7 + 0.35 * th / 90.0
        let rd = max 0.5 e.ROverD
        let b1 = if rd >= 1.0 then 0.21 / sqrt rd else 0.21 / Math.Pow(rd, 2.5)
        let zFric = f * (Math.PI * th / 180.0) * rd
        float e.Count * (a1 * b1 + zFric)
    let developedLength (l: Line) =
        List.sum l.Straights + (l.Elbows |> List.sumBy (elbowArc l.Id))
    let elbowCount (l: Line) = l.Elbows |> List.sumBy (fun e -> e.Count)
    let area (l: Line) = Math.PI * l.Id * l.Id / 4.0
    let totalArea (ls: Line list) = ls |> List.sumBy (fun l -> area l * float l.Count)
    let totalK (f: float) (l: Line) =
        f * developedLength l / l.Id
        + (l.Elbows |> List.sumBy (elbowK f))
        + l.ExtraK + 0.5 + 1.0
    let billOfMaterial (l: Line) =
        let st =
            l.Straights
            |> List.map (fun x -> sprintf "%.0f" (x * 1000.0))
            |> String.concat " + "
        let el =
            l.Elbows
            |> List.map (fun e -> sprintf "%d x %.0f° R/D %.1f" e.Count e.AngleDeg e.ROverD)
            |> String.concat " ; "
        sprintf "diritti %s mm | curve: %s" st (if el = "" then "nessuna" else el)



