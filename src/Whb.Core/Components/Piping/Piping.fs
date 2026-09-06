namespace Whb.Core

open System
open Constants
module Piping =
    /// <summary>Describes an elbow included in a piping route.</summary>
    type Elbow =
        { AngleDeg: float
          ROverD: float
          Count: int }
    /// <summary>Describes a nozzle-to-nozzle piping line and its hydraulic fittings.</summary>
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
    /// <summary>Creates a connected piping line from dimensions expressed in millimetres and metres.</summary>
    let line tag nps idMm count straights elbows extraK z ang note =
        { Tag = tag; Nps = nps; Id = idMm / 1000.0; Count = count
          Straights = straights; Elbows = elbows; ExtraK = extraK
          ZNozzle = z; AngleDeg = ang; Connected = true; Note = note }
    /// <summary>Marks a line as disconnected and appends the reason to its note.</summary>
    let blind (l: Line) (why: string) =
        { l with Connected = false
                 Note = (if l.Note = "" then why else l.Note + " - " + why) }
    /// <summary>Creates an elbow definition.</summary>
    let elbow ang rod n = { AngleDeg = ang; ROverD = rod; Count = n }
    /// <summary>Computes the developed arc length contributed by an elbow group.</summary>
    let elbowArc (d: float) (e: Elbow) =
        float e.Count * Math.PI * e.AngleDeg / 180.0 * e.ROverD * d
    /// <summary>Computes the elbow loss coefficient for a Darcy friction factor.</summary>
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
    /// <summary>Computes the developed length of a piping line.</summary>
    let developedLength (l: Line) =
        List.sum l.Straights + (l.Elbows |> List.sumBy (elbowArc l.Id))
    /// <summary>Counts the elbows installed on a line.</summary>
    let elbowCount (l: Line) = l.Elbows |> List.sumBy (fun e -> e.Count)
    /// <summary>Computes the internal flow area of a line.</summary>
    let area (l: Line) = Math.PI * l.Id * l.Id / 4.0
    /// <summary>Computes the total flow area of a collection of parallel lines.</summary>
    let totalArea (ls: Line list) = ls |> List.sumBy (fun l -> area l * float l.Count)
    /// <summary>Computes the total line loss coefficient including straight and local losses.</summary>
    let totalK (f: float) (l: Line) =
        f * developedLength l / l.Id
        + (l.Elbows |> List.sumBy (elbowK f))
        + l.ExtraK + 0.5 + 1.0
    /// <summary>Formats the straight and elbow dimensions of a line for a bill of material.</summary>
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



