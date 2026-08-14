namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants
open Types

module ReportCommon =

    let ci = CultureInfo.InvariantCulture
    let line = String('-', 96)
    let dline = String('=', 96)
    let hdr (sb: StringBuilder) (t: string) =
        sb.AppendLine().AppendLine(dline).AppendLine(t.ToUpperInvariant()).AppendLine(dline) |> ignore
    let kv (sb: StringBuilder) (k: string) (v: string) =
        sb.AppendLine(sprintf "  %-50s %s" k v) |> ignore
    let para (sb: StringBuilder) (indent: string) (txt: string) =
        let words = txt.Split(' ')
        let mutable cur = ""
        for wd in words do
            if cur.Length + wd.Length + 1 > 88 then
                sb.AppendLine(indent + cur) |> ignore
                cur <- wd
            else cur <- (if cur = "" then wd else cur + " " + wd)
        if cur <> "" then sb.AppendLine(indent + cur) |> ignore
    let legend (sb: StringBuilder) (items: (string * string) list) =
        sb.AppendLine() |> ignore
        sb.AppendLine("  LEGENDA DELLE COLONNE") |> ignore
        for (h, d) in items do
            let words = d.Split(' ')
            let mutable cur = ""
            let lines = ResizeArray<string>()
            for wd in words do
                if cur.Length + wd.Length + 1 > 74 then
                    lines.Add cur
                    cur <- wd
                else cur <- (if cur = "" then wd else cur + " " + wd)
            if cur <> "" then lines.Add cur
            sb.AppendLine(sprintf "    %-16s %s" h (if lines.Count > 0 then lines.[0] else "")) |> ignore
            for k in 1 .. lines.Count - 1 do
                sb.AppendLine(sprintf "    %-16s %s" "" lines.[k]) |> ignore
        sb.AppendLine() |> ignore
    let f0 (x: float) = x.ToString("F0", ci)
    let f1 (x: float) = x.ToString("F1", ci)
    let f2 (x: float) = x.ToString("F2", ci)
    let f3 (x: float) = x.ToString("F3", ci)
    let f4 (x: float) = x.ToString("F4", ci)
    let f5 (x: float) = x.ToString("F5", ci)

    /// <summary>
    /// Returns the material density used for inventory and shipping-weight estimates.
    /// </summary>
    /// <remarks>
    /// Densities are representative engineering values because the material catalogue does not currently store density.
    /// </remarks>
    let densityOf (mat: Materials.Material) =
        let name = mat.Name.ToUpperInvariant()
        if name.Contains("ALLOY") then 8050.0
        elif name.Contains("AUSTENITICO") || name.Contains("321") then 8000.0
        else 7850.0

    /// <summary>
    /// Returns the standard pipe outside diameter inferred from an NPS label.
    /// </summary>
    /// <remarks>
    /// The lookup is used only for metal-weight estimates when the model contains pipe ID but not pipe OD.
    /// </remarks>
    let pipeOdFromNps (nps: string) (id: float) =
        let s = nps.Replace("\"", "").Trim()
        let first = s.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead
        let n =
            match first with
            | Some v ->
                match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, x -> Some x
                | _ -> None
            | None -> None
        let odIn =
            match n with
            | Some 4.0 -> Some 4.5
            | Some 6.0 -> Some 6.625
            | Some 8.0 -> Some 8.625
            | Some 10.0 -> Some 10.75
            | Some 12.0 -> Some 12.75
            | Some 14.0 -> Some 14.0
            | Some 16.0 -> Some 16.0
            | Some 18.0 -> Some 18.0
            | Some 20.0 -> Some 20.0
            | Some 24.0 -> Some 24.0
            | _ -> None
        match odIn with
        | Some x -> max (id * 1.02) (x * 0.0254)
        | None -> id * 1.08

    /// <summary>
    /// Calculates the internal volume of a piping line.
    /// </summary>
    /// <remarks>
    /// The line volume includes straight lengths and elbow arc lengths for every parallel counted line.
    /// </remarks>
    let lineWaterVolume (l: Piping.Line) =
        Piping.area l * Piping.developedLength l * float l.Count

    /// <summary>
    /// Calculates the metal weight of a piping line.
    /// </summary>
    /// <remarks>
    /// Pipe OD is inferred from the NPS label when available; use vendor pipe weights for final material take-off.
    /// </remarks>
    let lineMetalWeight (rho: float) (l: Piping.Line) =
        let od = pipeOdFromNps l.Nps l.Id
        let areaMetal = Math.PI / 4.0 * max 0.0 (od * od - l.Id * l.Id)
        areaMetal * Piping.developedLength l * float l.Count * rho

    /// <summary>
    /// Calculates the liquid volume in a horizontal cylindrical drum up to the normal level.
    /// </summary>
    /// <remarks>
    /// The result is based on a circular-segment area times drum length and excludes internals displacement.
    /// </remarks>
    let drumWaterVolume (d: Drum.Internals) =
        let r = 0.5 * d.ShellId
        let h = max 0.0 (min d.ShellId d.NormalLevel)
        let theta = 2.0 * acos ((r - h) / r)
        let segmentArea = 0.5 * r * r * (theta - sin theta)
        segmentArea * d.Length

    type InventoryValues =
        { WhbWater: float
          RiserWater: float
          DowncomerWater: float
          DrumWater: float
          TotalWater: float
          TubeMetal: float
          ShellMetal: float
          BaffleMetal: float
          FerruleMetal: float
          RiserMetal: float
          DowncomerMetal: float
          DrumMetal: float
          BypassMetal: float
          TotalMetal: float }

    let inventoryValues (c: DesignCase) =
        let rhoTube = densityOf c.Material
        let rhoShell = densityOf c.ShellMaterial
        let rhoFerrule = densityOf c.FerruleMaterial
        let shellInternal = Math.PI / 4.0 * c.Tube.ShellId * c.Tube.ShellId * c.Tube.Length
        let tubeDisplacement = Math.PI / 4.0 * c.Tube.Do * c.Tube.Do * c.Tube.Length * float c.Tube.NTubes
        let bypassDisplacement =
            if c.Bypass.Enabled then Math.PI / 4.0 * c.Bypass.PipeOd * c.Bypass.PipeOd * c.Tube.Length else 0.0
        let whbWater = max 0.0 (shellInternal - tubeDisplacement - bypassDisplacement)
        let riserWater = c.Loop.Risers |> List.filter (fun l -> l.Connected) |> List.sumBy lineWaterVolume
        let downcomerWater = c.Loop.Downcomers |> List.filter (fun l -> l.Connected) |> List.sumBy lineWaterVolume
        let drumWater = if c.Loop.Drum.Enabled then drumWaterVolume c.Loop.Drum else 0.0
        let tubeMetal =
            Math.PI / 4.0 * (c.Tube.Do * c.Tube.Do - c.Tube.Di * c.Tube.Di) * c.Tube.Length * float c.Tube.NTubes * rhoTube
        let shellMetal =
            Math.PI / 4.0 * ((c.Tube.ShellId + 2.0 * c.ShellThickness) ** 2.0 - c.Tube.ShellId ** 2.0) * c.Tube.Length * rhoShell
        let baffleMetal =
            let count = max 0 (List.length c.BaffleSpans - 1)
            let gross = Math.PI / 4.0 * c.Tube.BaffleOd * c.Tube.BaffleOd
            let holes = Math.PI / 4.0 * c.Tube.Do * c.Tube.Do * float c.Tube.NTubes
            max 0.0 (gross - holes) * c.BaffleThickness * float count * rhoShell
        let ferruleMetal =
            if c.Ferrule.Enabled then
                let length = c.Ferrule.Lengths |> List.sumBy (fun (frac, len) -> frac * len)
                Math.PI / 4.0 * max 0.0 (c.Ferrule.SleeveOd ** 2.0 - c.Ferrule.Bore ** 2.0) * length * float c.Tube.NTubes * rhoFerrule
            else 0.0
        let riserMetal = c.Loop.Risers |> List.filter (fun l -> l.Connected) |> List.sumBy (lineMetalWeight rhoShell)
        let downcomerMetal = c.Loop.Downcomers |> List.filter (fun l -> l.Connected) |> List.sumBy (lineMetalWeight rhoShell)
        let drumMetal =
            if c.Loop.Drum.Enabled then
                let d = c.Loop.Drum
                Math.PI / 4.0 * ((d.ShellId + 2.0 * c.ShellThickness) ** 2.0 - d.ShellId ** 2.0) * d.Length * rhoShell
            else 0.0
        let bypassMetal =
            if c.Bypass.Enabled then
                let liner = Math.PI / 4.0 * (c.Bypass.LinerOd ** 2.0 - c.Bypass.LinerId ** 2.0) * c.Tube.Length * densityOf c.Bypass.LinerMaterial
                let pipe = Math.PI / 4.0 * (c.Bypass.PipeOd ** 2.0 - c.Bypass.InsulOd ** 2.0) * c.Tube.Length * densityOf c.Bypass.PipeMaterial
                liner + pipe
            else 0.0
        let totalWater = whbWater + riserWater + downcomerWater + drumWater
        let totalMetal = tubeMetal + shellMetal + baffleMetal + ferruleMetal + riserMetal + downcomerMetal + drumMetal + bypassMetal
        { WhbWater = whbWater
          RiserWater = riserWater
          DowncomerWater = downcomerWater
          DrumWater = drumWater
          TotalWater = totalWater
          TubeMetal = tubeMetal
          ShellMetal = shellMetal
          BaffleMetal = baffleMetal
          FerruleMetal = ferruleMetal
          RiserMetal = riserMetal
          DowncomerMetal = downcomerMetal
          DrumMetal = drumMetal
          BypassMetal = bypassMetal
          TotalMetal = totalMetal }

    let valvePositionLabel normalOpen minOpen maxOpen openDeg =
        if abs (openDeg - normalOpen) < 0.05 then "NORMALE"
        elif abs (openDeg - minOpen) < 0.05 then "MINIMO"
        elif abs (openDeg - maxOpen) < 0.05 then "MASSIMO"
        else ""

    /// <summary>
    /// Builds a text summary of water volumes and estimated metal weights.
    /// </summary>
    /// <remarks>
    /// The summary separates water inventory by WHB shell, risers, downcomers, and steam drum, then lists component metal-weight estimates.
    /// </remarks>
    let inventoryText (r: DesignResult) =
        let v = inventoryValues r.Case

        let sb = StringBuilder()
        hdr sb "Water Volume And Metal Weight Summary"
        sb.AppendLine("  Water volumes are geometric inventories. Riser volume is total internal volume, not separated into liquid/vapor holdup.") |> ignore
        sb.AppendLine("  Metal weights are estimates. Riser/downcomer pipe OD is inferred from NPS; use vendor MTO values for final weights.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Water inventory" "m3" "% total") |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "WHB shell side" (f3 v.WhbWater) (f1 (100.0 * v.WhbWater / v.TotalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Risers" (f3 v.RiserWater) (f1 (100.0 * v.RiserWater / v.TotalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Downcomers" (f3 v.DowncomerWater) (f1 (100.0 * v.DowncomerWater / v.TotalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Steam drum at normal level" (f3 v.DrumWater) (f1 (100.0 * v.DrumWater / v.TotalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "TOTAL WATER" (f3 v.TotalWater) "100.0") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "  %-32s %12s" "Metal component" "kg") |> ignore
        for name, value in
            [ "Tubes", v.TubeMetal
              "WHB shell", v.ShellMetal
              "Baffles", v.BaffleMetal
              "Ferrules", v.FerruleMetal
              "Risers", v.RiserMetal
              "Downcomers", v.DowncomerMetal
              "Steam drum shell", v.DrumMetal
              "Bypass liner and pipe", v.BypassMetal
              "TOTAL METAL", v.TotalMetal ] do
            sb.AppendLine(sprintf "  %-32s %12s" name (f0 value)) |> ignore
        sb.ToString()

    /// <summary>
    /// Builds a CSV summary of water volumes and estimated metal weights.
    /// </summary>
    /// <remarks>
    /// The CSV table mirrors the text inventory report for spreadsheet checks.
    /// </remarks>
    let inventoryCsv (r: DesignResult) =
        let v = inventoryValues r.Case
        let sb = StringBuilder()
        sb.AppendLine("section,item,unit,value,note") |> ignore
        for name, value, note in
            [ "WHB shell side", v.WhbWater, "Geometric shell-side water volume excluding tubes and bypass pipe"
              "Risers", v.RiserWater, "Total connected riser internal volume"
              "Downcomers", v.DowncomerWater, "Total connected downcomer internal volume"
              "Steam drum at normal level", v.DrumWater, "Liquid volume up to normal level; internals displacement excluded"
              "TOTAL WATER", v.TotalWater, "Total geometric water inventory" ] do
            sb.AppendLine(sprintf "water,%s,m3,%s,%s" name (f3 value) note) |> ignore
        for name, value, note in
            [ "Tubes", v.TubeMetal, "Exact from tube OD, ID, length, count, and representative density"
              "WHB shell", v.ShellMetal, "Cylindrical shell estimate"
              "Baffles", v.BaffleMetal, "Plate estimate excluding tube holes"
              "Ferrules", v.FerruleMetal, "Sleeve estimate"
              "Risers", v.RiserMetal, "Pipe OD inferred from NPS"
              "Downcomers", v.DowncomerMetal, "Pipe OD inferred from NPS"
              "Steam drum shell", v.DrumMetal, "Cylindrical shell estimate"
              "Bypass liner and pipe", v.BypassMetal, "Liner plus outer pipe estimate"
              "TOTAL METAL", v.TotalMetal, "Estimated total metal weight" ] do
            sb.AppendLine(sprintf "metal,%s,kg,%s,%s" name (f0 value) note) |> ignore
        sb.ToString()
    let internal definizioni (sb: StringBuilder) =
        sb.AppendLine() |> ignore
        sb.AppendLine("  DEFINIZIONI ESSENZIALI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        sb.AppendLine("  CR - RAPPORTO DI CIRCOLAZIONE") |> ignore
        sb.AppendLine("     CR = portata d'acqua che circola nel mantello / portata di vapore prodotta.") |> ignore
        sb.AppendLine("     Adimensionale. Equivale al reciproco del titolo in uscita dal fascio: CR = 10") |> ignore
        sb.AppendLine("     significa che di ogni 10 kg di miscela che escono dal mantello 1 kg e' vapore e") |> ignore
        sb.AppendLine("     9 kg sono acqua che torna al corpo cilindrico. NON e' un dato di progetto: e' il") |> ignore
        sb.AppendLine("     risultato dell'equilibrio fra il battente motore, generato dalla differenza di") |> ignore
        sb.AppendLine("     peso fra la colonna che scende e quelle che salgono, e le perdite di carico del") |> ignore
        sb.AppendLine("     giro. Serve che sia alto perche' l'acqua in eccesso e' quella che LAVA i tubi e") |> ignore
        sb.AppendLine("     impedisce che la parete si scopra. Criterio pratico: CR >= 10, cioe' titolo <= 0.10.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  DNB - DEPARTURE FROM NUCLEATE BOILING (crisi di ebollizione)") |> ignore
        sb.AppendLine("     E' il FENOMENO, non un numero. In ebollizione nucleata le bolle si staccano dalla") |> ignore
        sb.AppendLine("     parete e l'acqua le rimpiazza subito: lo scambio e' ottimo e il metallo resta") |> ignore
        sb.AppendLine("     freddo. Se il flusso termico cresce oltre un valore critico, le bolle si generano") |> ignore
        sb.AppendLine("     piu' in fretta di quanto l'acqua riesca a rimpiazzarle e si saldano in un FILM DI") |> ignore
        sb.AppendLine("     VAPORE continuo che isola la parete. Il vapore conduce male, quindi lo scambio") |> ignore
        sb.AppendLine("     crolla e la temperatura del metallo sale di centinaia di gradi in pochi secondi.") |> ignore
        sb.AppendLine("     Nei fasci il fenomeno si chiama anche STEAM BLANKETING. Non e' progressivo: e'") |> ignore
        sb.AppendLine("     un salto, e il tubo si rompe per cedimento a caldo prima che ce ne si accorga.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  CHF - CRITICAL HEAT FLUX (flusso termico critico)") |> ignore
        sb.AppendLine("     E' il valore di flusso termico al quale si innesca il DNB. Dipende dalla") |> ignore
        sb.AppendLine("     pressione, dalla geometria del fascio e soprattutto dal TITOLO locale: piu'") |> ignore
        sb.AppendLine("     vapore c'e' gia' nella miscela che lava il tubo, meno flusso serve per scoprire") |> ignore
        sb.AppendLine("     la parete.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  DNBR - DEPARTURE FROM NUCLEATE BOILING RATIO") |> ignore
        sb.AppendLine("     DNBR = flusso termico critico locale / flusso termico effettivo locale.") |> ignore
        sb.AppendLine("     E' un MARGINE, adimensionale: DNBR = 3 vuol dire che si lavora a un terzo del") |> ignore
        sb.AppendLine("     limite, DNBR = 1 esattamente al limite, DNBR < 1 oltre il limite. La pratica di") |> ignore
        sb.AppendLine("     progetto chiede almeno 2. Si calcola CELLA PER CELLA, perche' sia il flusso sia") |> ignore
        sb.AppendLine("     il valore critico cambiano lungo il tubo e da banda a banda: il minimo non cade") |> ignore
        sb.AppendLine("     dove il flusso e' massimo, ma dove il rapporto fra i due e' peggiore.") |> ignore
        sb.AppendLine("     AVVERTENZA: nessuno dei modelli di CHF disponibili in letteratura e' tarato su") |> ignore
        sb.AppendLine("     questa geometria a questa pressione, e fra loro divergono di un ordine di") |> ignore
        sb.AppendLine("     grandezza. Il DNBR va quindi usato per confronti RELATIVI - fra zone dello stesso") |> ignore
        sb.AppendLine("     apparecchio o fra varianti dello stesso progetto - non come numero assoluto.") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        sb.AppendLine() |> ignore



