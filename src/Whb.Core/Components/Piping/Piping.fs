namespace Whb.Core

open System
open Constants

/// Tubazioni del circuito di circolazione descritte come nella distinta di
/// un disegno isometrico: **tratti diritti** + **curve di vario angolo**.
/// La perdita di carico viene calcolata sulla lunghezza sviluppata reale
/// (diritti + archi delle curve) piu' i coefficienti localizzati delle curve,
/// invece che su una lunghezza equivalente stimata a occhio.
module Piping =

    /// Curva: angolo [gradi], raggio/diametro, quantita' nella linea
    type Elbow =
        { AngleDeg: float
          ROverD: float
          Count: int }

    /// Una linea del circuito (un bocchello e la sua tubazione).
    type Line =
        { /// Sigla del bocchello (R1, DC1, ...)
          Tag: string
          /// Diametro nominale come da distinta
          Nps: string
          /// Diametro interno [m]
          Id: float
          /// Numero di linee identiche rappresentate da questa voce
          Count: int
          /// Tratti diritti [m]
          Straights: float list
          /// Curve
          Elbows: Elbow list
          /// Coefficienti localizzati aggiuntivi (valvole, tee, imbocchi speciali)
          ExtraK: float
          /// Posizione assiale del bocchello sul mantello [m] dalla piastra calda
          ZNozzle: float
          /// Posizione angolare [gradi]: 0 = cielo, 180 = fondo
          AngleDeg: float
          /// **false** = bocchello presente sull'apparecchio ma NON collegato
          /// (flangia cieca / linea non realizzata). Non partecipa
          /// all'idraulica del circuito, ma resta in distinta perche' esiste
          /// fisicamente ed e' una riserva di progetto.
          Connected: bool
          /// Note (per esempio la fonte del dato)
          Note: string }

    /// Crea una linea collegata convertendo il diametro interno da millimetri a metri.
    let line tag nps idMm count straights elbows extraK z ang note =
        { Tag = tag; Nps = nps; Id = idMm / 1000.0; Count = count
          Straights = straights; Elbows = elbows; ExtraK = extraK
          ZNozzle = z; AngleDeg = ang; Connected = true; Note = note }

    /// Bocchello esistente ma non collegato
    let blind (l: Line) (why: string) =
        { l with Connected = false
                 Note = (if l.Note = "" then why else l.Note + " - " + why) }

    /// Crea una voce di curva con angolo, raggio relativo e quantita'.
    let elbow ang rod n = { AngleDeg = ang; ROverD = rod; Count = n }

    /// Lunghezza dell'arco di una curva [m]
    let elbowArc (d: float) (e: Elbow) =
        float e.Count * Math.PI * e.AngleDeg / 180.0 * e.ROverD * d

    /// Coefficiente di perdita di una curva liscia - metodo di Idelchik:
    ///   zeta = A1 * B1 + zeta_attrito
    ///   A1 (angolo): 0.9 sin(theta) per theta < 70°, 1.0 a 90°,
    ///                0.7 + 0.35 theta/90 oltre 100°
    ///   B1 (raggio): 0.21 / (R/D)^0.5  per R/D >= 1
    ///   zeta_attrito = f * (pi*theta/180) * (R/D)
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

    /// Lunghezza sviluppata della linea [m] (diritti + archi)
    let developedLength (l: Line) =
        List.sum l.Straights + (l.Elbows |> List.sumBy (elbowArc l.Id))

    /// Numero totale di curve
    let elbowCount (l: Line) = l.Elbows |> List.sumBy (fun e -> e.Count)

    /// Sezione di passaggio della singola linea [m²]
    let area (l: Line) = Math.PI * l.Id * l.Id / 4.0

    /// Sezione totale di un insieme di linee [m²]
    let totalArea (ls: Line list) = ls |> List.sumBy (fun l -> area l * float l.Count)

    /// Coefficiente di resistenza complessivo della linea, riferito alla
    /// velocita' nella linea: K_tot = f*L_dev/D + somma K curve + K extra
    /// + imbocco (0.5) + sbocco (1.0).
    let totalK (f: float) (l: Line) =
        f * developedLength l / l.Id
        + (l.Elbows |> List.sumBy (elbowK f))
        + l.ExtraK + 0.5 + 1.0

    /// Descrizione compatta della distinta della linea
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
