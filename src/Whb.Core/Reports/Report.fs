namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants
open Types

/// <summary>
/// Provides report functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Report =

    /// <summary>
    /// Calculates or returns ci for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private ci = CultureInfo.InvariantCulture
    /// <summary>
    /// Calculates or returns line for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private line = String('-', 96)
    /// <summary>
    /// Calculates or returns dline for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private dline = String('=', 96)

    /// <summary>
    /// Calculates or returns hdr for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private hdr (sb: StringBuilder) (t: string) =
        sb.AppendLine().AppendLine(dline).AppendLine(t.ToUpperInvariant()).AppendLine(dline) |> ignore

    /// <summary>
    /// Calculates or returns kv for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private kv (sb: StringBuilder) (k: string) (v: string) =
        sb.AppendLine(sprintf "  %-50s %s" k v) |> ignore

    /// <summary>
    /// Calculates or returns para for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private para (sb: StringBuilder) (indent: string) (txt: string) =
        let words = txt.Split(' ')
        let mutable cur = ""
        for wd in words do
            if cur.Length + wd.Length + 1 > 88 then
                sb.AppendLine(indent + cur) |> ignore
                cur <- wd
            else cur <- (if cur = "" then wd else cur + " " + wd)
        if cur <> "" then sb.AppendLine(indent + cur) |> ignore

    /// <summary>
    /// Calculates or returns legend for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private legend (sb: StringBuilder) (items: (string * string) list) =
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

    /// <summary>
    /// Calculates or returns f0 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f0 (x: float) = x.ToString("F0", ci)
    /// <summary>
    /// Calculates or returns f1 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f1 (x: float) = x.ToString("F1", ci)
    /// <summary>
    /// Calculates or returns f2 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f2 (x: float) = x.ToString("F2", ci)
    /// <summary>
    /// Calculates or returns f3 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f3 (x: float) = x.ToString("F3", ci)
    /// <summary>
    /// Calculates or returns f4 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f4 (x: float) = x.ToString("F4", ci)
    /// <summary>
    /// Calculates or returns f5 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f5 (x: float) = x.ToString("F5", ci)

    /// <summary>
    /// Returns the material density used for inventory and shipping-weight estimates.
    /// </summary>
    /// <remarks>
    /// Densities are representative engineering values because the material catalogue does not currently store density.
    /// </remarks>
    let private densityOf (mat: Materials.Material) =
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
    let private pipeOdFromNps (nps: string) (id: float) =
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
    let private lineWaterVolume (l: Piping.Line) =
        Piping.area l * Piping.developedLength l * float l.Count

    /// <summary>
    /// Calculates the metal weight of a piping line.
    /// </summary>
    /// <remarks>
    /// Pipe OD is inferred from the NPS label when available; use vendor pipe weights for final material take-off.
    /// </remarks>
    let private lineMetalWeight (rho: float) (l: Piping.Line) =
        let od = pipeOdFromNps l.Nps l.Id
        let areaMetal = Math.PI / 4.0 * max 0.0 (od * od - l.Id * l.Id)
        areaMetal * Piping.developedLength l * float l.Count * rho

    /// <summary>
    /// Calculates the liquid volume in a horizontal cylindrical drum up to the normal level.
    /// </summary>
    /// <remarks>
    /// The result is based on a circular-segment area times drum length and excludes internals displacement.
    /// </remarks>
    let private drumWaterVolume (d: Drum.Internals) =
        let r = 0.5 * d.ShellId
        let h = max 0.0 (min d.ShellId d.NormalLevel)
        let theta = 2.0 * acos ((r - h) / r)
        let segmentArea = 0.5 * r * r * (theta - sin theta)
        segmentArea * d.Length

    /// <summary>
    /// Builds a text summary of water volumes and estimated metal weights.
    /// </summary>
    /// <remarks>
    /// The summary separates water inventory by WHB shell, risers, downcomers, and steam drum, then lists component metal-weight estimates.
    /// </remarks>
    let inventoryText (r: DesignResult) =
        let c = r.Case
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
        let totalWater = whbWater + riserWater + downcomerWater + drumWater

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
        let totalMetal = tubeMetal + shellMetal + baffleMetal + ferruleMetal + riserMetal + downcomerMetal + drumMetal + bypassMetal

        let sb = StringBuilder()
        hdr sb "Water Volume And Metal Weight Summary"
        sb.AppendLine("  Water volumes are geometric inventories. Riser volume is total internal volume, not separated into liquid/vapor holdup.") |> ignore
        sb.AppendLine("  Metal weights are estimates. Riser/downcomer pipe OD is inferred from NPS; use vendor MTO values for final weights.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Water inventory" "m3" "% total") |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "WHB shell side" (f3 whbWater) (f1 (100.0 * whbWater / totalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Risers" (f3 riserWater) (f1 (100.0 * riserWater / totalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Downcomers" (f3 downcomerWater) (f1 (100.0 * downcomerWater / totalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "Steam drum at normal level" (f3 drumWater) (f1 (100.0 * drumWater / totalWater))) |> ignore
        sb.AppendLine(sprintf "  %-32s %12s %12s" "TOTAL WATER" (f3 totalWater) "100.0") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "  %-32s %12s" "Metal component" "kg") |> ignore
        for name, value in
            [ "Tubes", tubeMetal
              "WHB shell", shellMetal
              "Baffles", baffleMetal
              "Ferrules", ferruleMetal
              "Risers", riserMetal
              "Downcomers", downcomerMetal
              "Steam drum shell", drumMetal
              "Bypass liner and pipe", bypassMetal
              "TOTAL METAL", totalMetal ] do
            sb.AppendLine(sprintf "  %-32s %12s" name (f0 value)) |> ignore
        sb.ToString()

    /// <summary>
    /// Builds a CSV summary of water volumes and estimated metal weights.
    /// </summary>
    /// <remarks>
    /// The CSV table mirrors the text inventory report for spreadsheet checks.
    /// </remarks>
    let inventoryCsv (r: DesignResult) =
        let c = r.Case
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
        let sb = StringBuilder()
        sb.AppendLine("section,item,unit,value,note") |> ignore
        for name, value, note in
            [ "WHB shell side", whbWater, "Geometric shell-side water volume excluding tubes and bypass pipe"
              "Risers", riserWater, "Total connected riser internal volume"
              "Downcomers", downcomerWater, "Total connected downcomer internal volume"
              "Steam drum at normal level", drumWater, "Liquid volume up to normal level; internals displacement excluded"
              "TOTAL WATER", whbWater + riserWater + downcomerWater + drumWater, "Total geometric water inventory" ] do
            sb.AppendLine(sprintf "water,%s,m3,%s,%s" name (f3 value) note) |> ignore
        for name, value, note in
            [ "Tubes", tubeMetal, "Exact from tube OD, ID, length, count, and representative density"
              "WHB shell", shellMetal, "Cylindrical shell estimate"
              "Baffles", baffleMetal, "Plate estimate excluding tube holes"
              "Ferrules", ferruleMetal, "Sleeve estimate"
              "Risers", riserMetal, "Pipe OD inferred from NPS"
              "Downcomers", downcomerMetal, "Pipe OD inferred from NPS"
              "Steam drum shell", drumMetal, "Cylindrical shell estimate"
              "Bypass liner and pipe", bypassMetal, "Liner plus outer pipe estimate"
              "TOTAL METAL", tubeMetal + shellMetal + baffleMetal + ferruleMetal + riserMetal + downcomerMetal + drumMetal + bypassMetal, "Estimated total metal weight" ] do
            sb.AppendLine(sprintf "metal,%s,kg,%s,%s" name (f0 value) note) |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns definizioni for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let definizioni (sb: StringBuilder) =
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


    /// <summary>
    /// Calculates or returns text for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let text (r: DesignResult) =
        let c = r.Case
        let sb = StringBuilder()
        let cells = r.Cells
        let hot = cells |> List.filter (fun x -> not x.InFerrule)
        let ny = List.length r.Bands

        sb.AppendLine(dline) |> ignore
        sb.AppendLine("WHB / PGC A TUBI DA FUMO - CALCOLO TERMICO E IDRAULICO 2-D") |> ignore
        sb.AppendLine(sprintf "Caso: %s" c.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine(sprintf "Discretizzazione: %d sezioni assiali x %d bande orizzontali = %d celle"
                          c.NZ ny (c.NZ * ny)) |> ignore
        sb.AppendLine(dline) |> ignore

        hdr sb "0. Ipotesi principali e stato dei dati"
        para sb "  " "Questa sezione elenca tutto cio' su cui il calcolo si regge. Ogni voce e' classificata come DATO (verificato su documentazione di impianto), CONFERMATO (comunicato dal committente), ASSUNTO (scelta del calcolo, da confermare) o APERTO (manca il dato, ed e' indicato l'effetto). Tutti i numeri del report discendono da queste voci."
        sb.AppendLine() |> ignore
        let ipo (stato: string) (voce: string) (valore: string) (nota: string) =
            sb.AppendLine(sprintf "  [%-10s] %-42s %s" stato voce valore) |> ignore
            if nota <> "" then para sb "               " nota
        sb.AppendLine("  --- GEOMETRIA E MATERIALI ---") |> ignore
        ipo "DATO" "Tubi" (sprintf "%d x OD %.1f x %.2f, L %.3f m, passo %.1f triangolare 60°" c.Tube.NTubes (c.Tube.Do*1000.0) ((c.Tube.Do-c.Tube.Di)*500.0) c.Tube.Length (c.Tube.Pitch*1000.0)) ""
        ipo "DATO" "Mantello / OTL / ITL" (sprintf "ID %.0f (WT %.0f) / %.1f / %.0f mm" (c.Tube.ShellId*1000.0) (c.ShellThickness*1000.0) (c.Tube.Otl*1000.0) (c.Tube.Itl*1000.0)) ""
        ipo "CONFERMATO" "Materiale tubi" c.Material.Name "Comunicato dal committente. Determina k (temperatura metallica), alpha (dilatazioni), E e Sy (tensioni)."
        ipo "ASSUNTO" "Materiale mantello" c.ShellMaterial.Name "Coerente con la classe di pressione e temperatura; da confermare sul certificato."
        ipo "DATO" "Ferrula" (sprintf "bore %.1f, manicotto OD %.1f, sporgenza %s mm" (c.Ferrule.Bore*1000.0) (c.Ferrule.SleeveOd*1000.0) (c.Ferrule.Lengths |> List.map (fun (_,l) -> sprintf "%.0f" (l*1000.0)) |> String.concat "/")) "Da disegno di dettaglio. La lunghezza governa il picco di flusso: e' la principale leva di progetto."
        ipo "CONFERMATO" "Gioco foro diaframma / OD tubo" "0.40 mm sul diametro" "0.20 mm radiali, cioe' lo 0.5 % del raggio: il vincolo radiale e' effettivo. Giustifica sia l'incastro nel calcolo di frequenza propria sia il ruolo di anello di irrigidimento contro la pressione esterna."
        ipo "ASSUNTO" "Passo diaframmi (campata governante)" (sprintf "%.2f m" c.UnsupportedSpan) "Il passo reale e' VARIABILE lungo l'apparecchio. Qui si assume il valore governante. E' il parametro piu' sensibile per le vibrazioni: il rapporto V/Vcrit cresce con il QUADRATO della campata."
        sb.AppendLine() |> ignore
        sb.AppendLine("  --- PROCESSO ---") |> ignore
        ipo "DATO" "Portata gas" (sprintf "%.2f kg/s (datasheet x 1.10)" c.Gas.MassFlow) "La maggiorazione del 10 % e' quella prescritta dal datasheet."
        ipo "DATO" "T / p ingresso gas" (sprintf "%.1f °C / %.2f bar a" (kToC c.Gas.TIn) (paToBar c.Gas.PIn)) ""
        ipo "DATO" "Pressione corpo cilindrico" (sprintf "%.2f bar a  (Tsat %.2f °C)" (paToBar c.Water.DrumPressure) (kToC r.Sat.Tsat)) ""
        ipo "DATO" "Sporcamento gas / acqua" (sprintf "%.5f / %.5f m2K/W" c.Gas.FoulingIn c.Water.FoulingOut) "CONDIZIONE SPORCA di progetto, confermata. Il confronto con la condizione pulita e' nella sezione 5c e ribalta la lettura del rischio: da pulito il DNBR e' peggiore."
        ipo "ASSUNTO" "Reazione di shift" (Shift.modeName c.Gas.ShiftMode) "Congelata: senza catalizzatore la cinetica e' trascurabile sotto i 900 °C. L'accordo con il datasheet allo 0.06 % conferma indirettamente l'ipotesi."
        sb.AppendLine() |> ignore
        sb.AppendLine("  --- MODELLI ---") |> ignore
        ipo "ASSUNTO" "Correlazione lato gas" (GasSide.correlationName c.Gas.Correlation) "E' la resistenza dominante: la scelta della correlazione vale +/- 23 K sulla temperatura metallica (sezione 5e)."
        ipo "ASSUNTO" "Miscelazione mu e k" (GasProps.mixingRuleName c.Gas.MixingRule) "Wilke e' la scelta fisicamente corretta per miscele ricche di idrogeno. La media molare, usata da molti datasheet, e' disponibile per confronto."
        ipo "ASSUNTO" "Gas reale" (if c.Gas.RealGas then "SI - secondo viriale, B(H2O) da IAPWS-IF97" else "NO - gas ideale") "Chiude lo scarto sul datasheet da -0.93 % a -0.06 %. Il termine dominante e' l'acqua, che pesa y^2 = 0.106 nella regola esatta."
        ipo "ASSUNTO" "Ebollizione" (WaterSide.poolBoilingName c.Water.Correlation) (sprintf "con fattore di fascio di Palen Fb = %.1f. La scelta e' poco influente: sposta il flusso dell'1 %%." c.Water.BundleFactor)
        ipo "ASSUNTO" "Distribuzione lato mantello" "rapporto di circolazione locale uniforme" "Il mantello e' trattato come volume unico con plenum continui. E' la semplificazione strutturale principale del lato acqua."
        ipo "ASSUNTO" "Costanti di vibrazione" "K di Connors = 3.0, decremento log. = 0.03" "Combinazione PIU' CONSERVATIVA del campo sperimentale (K 3.0-10, delta 0.03-0.10). Con K = 4.5 e delta = 0.06 la velocita' critica raddoppia. E' l'incertezza dominante della verifica FIV."
        sb.AppendLine() |> ignore
        sb.AppendLine("  --- CIRCUITO ---") |> ignore
        ipo "CONFERMATO" "Quota asse drum - asse WHB" (sprintf "%.2f m" c.Loop.DzDrumWhb) "Confermata dal committente: e' il termine che genera tutto il battente motore."
        ipo "DATO" "Livello normale nel drum" (sprintf "%.0f mm dal fondo (ID %.0f)" (c.Loop.Drum.NormalLevel*1000.0) (c.Loop.Drum.ShellId*1000.0)) "Da datasheet del corpo cilindrico."
        ipo "CONFERMATO" "Bocchelli non collegati" (r.LineChecks |> List.filter (fun l -> not l.Connected) |> List.map (fun l -> l.Tag) |> String.concat ", ") "Esclusi dall'idraulica. Erano alle due estremita' dell'apparecchio, cioe' dove il campo tubi e' meno lavato."
        ipo "APERTO" "K del convogliatore del drum" (sprintf "assunto %.1f -> %.1f mbar" c.Loop.Drum.ConvExtraK (match r.DrumResult with Some d -> d.DpCirculation/100.0 | None -> 0.0)) "E' l'ultima assunzione aperta sulla circolazione. Campo ragionevole 0.5-3.0, che corrisponde a 0-51 mbar e a un rapporto di circolazione fra 12 e 8. Serve la curva sperimentale del costruttore del corpo cilindrico."
        ipo "APERTO" "Posizioni assiali dei bocchelli" "di primo tentativo" "Non incidono sul bilancio globale ma sulla distribuzione assiale del lavaggio."
        ipo "APERTO" "Spessore tubo di contenimento by-pass" (sprintf "assunto %.1f mm da disegno" ((c.Bypass.PipeOd - c.Bypass.InsulOd)*500.0)) "Con questo spessore la verifica a pressione esterna NON passa (sezione 8e). E' l'unico risultato non conforme del calcolo e dipende da un dato da confermare."
        sb.AppendLine() |> ignore
        para sb "  " "LIMITI DICHIARATI. Il calcolo e' stazionario e monodimensionale nel tubo, bidimensionale sul mantello (assiale x bande). Non contiene: verifica di codice della piastra tubiera (UHX-13), verifica a pressione esterna con carico di punta secondo codice, simulazione dinamica dei transitori, distribuzione tridimensionale del gas nella camera d'ingresso. Le sezioni corrispondenti sono screening dichiarati come tali."

        hdr sb "1. Geometria"
        kv sb "Tubi" (sprintf "%d x OD %s x %s WT (ID %s) x L %s m"
                          c.Tube.NTubes (f1 (c.Tube.Do * 1000.0))
                          (f2 ((c.Tube.Do - c.Tube.Di) * 500.0)) (f1 (c.Tube.Di * 1000.0)) (f3 c.Tube.Length))
        kv sb "Passo / layout" (sprintf "%s mm - triangolare 60°" (f1 (c.Tube.Pitch * 1000.0)))
        kv sb "Mantello ID / OTL / ITL" (sprintf "%s / %s / %s mm"
                                             (f1 (c.Tube.ShellId * 1000.0)) (f2 (c.Tube.Otl * 1000.0)) (f1 (c.Tube.Itl * 1000.0)))
        kv sb "Superficie esterna / interna" (sprintf "%s / %s m2" (f1 r.AreaOut) (f1 r.AreaIn))
        kv sb "Materiale tubi" c.Material.Name
        kv sb "Diaframmi di supporto OD" (sprintf "%s mm -> corona anulare aperta %s m2/m"
                                              (f0 (c.Tube.BaffleOd * 1000.0))
                                              (f4 (Bundle.openAnnulusArea c.Tube.ShellId c.Tube.BaffleOd c.Tube.Otl)))
        kv sb "Ferrula" (if c.Ferrule.Enabled then
                            sprintf "bore %s / manicotto OD %s / isolante fino a ID tubo %s mm"
                                (f1 (c.Ferrule.Bore * 1000.0))
                                (f1 (c.Ferrule.SleeveOd * 1000.0)) (f1 (c.Tube.Di * 1000.0))
                         else "assente")
        if c.Ferrule.Enabled then
            kv sb "  classi di lunghezza (frazione x mm)"
                (BundleSolver.ferruleClasses c.Ferrule
                 |> List.map (fun (fr, l) -> sprintf "%.0f%% x %s" (fr * 100.0) (f0 (l * 1000.0)))
                 |> String.concat " | ")
        if c.Ferrule.Enabled then
            let rF = BundleSolver.ferruleResistance c.Ferrule c.Tube.Di 500.0
            kv sb "  resistenza ferrula a 500 °C" (sprintf "%s m·K/W" (f4 rF))
            let ferruleLength =
                BundleSolver.ferruleClasses c.Ferrule
                |> List.sumBy (fun (fr, l) -> fr * l)
            let compIn = GasProps.normalize c.Gas.Composition
            let propsIn = GasProps.mixReal c.Gas.MixingRule c.Gas.RealGas compIn c.Gas.TIn c.Gas.PIn c.Gas.Z
            let mdotPerTube = c.Gas.MassFlow / float c.Tube.NTubes
            let dpFerrule =
                BundleSolver.ferrulePressureDropEstimate
                    c.Ferrule c.Tube.Di c.Tube.Roughness mdotPerTube propsIn ferruleLength
            let paperThk = BundleSolver.ferruleInsulationThickness c.Ferrule c.Tube.Di
            kv sb "  perdita pressione ferrula stimata" (sprintf "%s mbar per tubo" (f2 (dpFerrule / 100.0)))
            kv sb "  spessore carta isolante radiale" (sprintf "%s mm - %s" (f2 (paperThk * 1000.0)) (BundleSolver.ferruleInsulationFitStatus c.Ferrule c.Tube.Di))
        kv sb "Bande: n. tubi (dal basso verso l'alto)"
            (r.Bands |> List.map (fun b -> f0 b.NTubes) |> String.concat " / ")
        kv sb "  verifica somma tubi" (f1 (Bundle.totalTubes r.Bands))

        para sb "  " "IN PAROLE SEMPLICI. Questa sezione descrive il pezzo di ferro. I tubi portano il gas caldo dentro; il mantello e' il barile che li contiene ed e' pieno d'acqua che bolle. OTL e' il diametro del cerchio che racchiude tutti i tubi, ITL il foro centrale lasciato senza tubi. I diaframmi sono i dischi forati che sostengono i tubi lungo la lunghezza e ne impediscono la vibrazione: se il loro diametro e' minore di quello del mantello resta una corona anulare libera, che diventa una scorciatoia per l'acqua. La ferrula e' la boccola isolante infilata nella bocca di ogni tubo, che protegge l'imbocco dal gas piu' caldo. La suddivisione in bande serve al calcolo: il fascio viene tagliato in fette orizzontali perche' l'acqua lo attraversa dal basso verso l'alto."
        sb.AppendLine() |> ignore

        hdr sb "2. Condizioni di processo"
        let comp = GasProps.normalize c.Gas.Composition
        kv sb "Composizione gas ingresso (%mol)"
            (comp |> List.map (fun (s, y) -> sprintf "%A %s" s (f2 (y * 100.0))) |> String.concat "  ")
        kv sb "Massa molare miscela" (sprintf "%s kg/kmol" (f2 (GasProps.mixMolarMass comp * 1000.0)))
        kv sb "Water-gas shift" (Shift.modeName c.Gas.ShiftMode)
        kv sb "Portata gas" (sprintf "%s kg/s  (%s kg/h)" (f2 c.Gas.MassFlow) (f0 (c.Gas.MassFlow * 3600.0)))
        kv sb "Temperatura gas IN" (sprintf "%s °C" (f1 (kToC c.Gas.TIn)))
        kv sb "Temperatura gas OUT (media / min / max)"
            (sprintf "%s / %s / %s °C" (f1 (kToC r.TGasOutMean)) (f1 (kToC r.TGasOutMin)) (f1 (kToC r.TGasOutMax)))
        kv sb "Pressione gas IN / dP" (sprintf "%s bar(a) / %s bar" (f2 (paToBar c.Gas.PIn)) (f3 (r.DpGas / 1e5)))
        kv sb "Fouling gas / acqua" (sprintf "%s / %s m2K/W" (f5 c.Gas.FoulingIn) (f5 c.Water.FoulingOut))
        kv sb "Pressione vapore (drum)" (sprintf "%s bar(a)  ->  Tsat = %s °C"
                                             (f2 (paToBar r.Sat.P)) (f2 (kToC r.Sat.Tsat)))
        kv sb "hfg / rho_l / rho_v" (sprintf "%s kJ/kg | %s | %s kg/m3"
                                         (f1 (r.Sat.Hfg / 1000.0)) (f1 r.Sat.RhoL) (f2 r.Sat.RhoV))

        para sb "  " "IN PAROLE SEMPLICI. Qui ci sono i dati di ingresso del processo: che gas passa nei tubi, quanto ne passa, a che temperatura e pressione entra, e a che pressione sta l'acqua nel corpo cilindrico. Tsat e' la temperatura a cui l'acqua bolle a quella pressione: e' un valore fisso, la caldaia lavora tutta a quella temperatura sul lato acqua. hfg e' il calore che serve per trasformare un chilo di acqua in vapore. Il fouling e' lo sporco che si deposita sulle due facce del tubo, ed e' un dato di progetto (piu' e' alto, piu' superficie serve). Water-gas shift indica se si e' tenuto conto della reazione chimica CO + H2O -> CO2 + H2 lungo il tubo: nei WHB senza catalizzatore e' congelata, cioe' non avviene."
        sb.AppendLine() |> ignore

        hdr sb "3. Prestazioni globali"
        kv sb "POTENZA SCAMBIATA" (sprintf "%s MW" (f3 (r.Duty / 1e6)))
        kv sb "PRODUZIONE DI VAPORE" (sprintf "%s kg/s  (%s kg/h)" (f2 r.SteamProduction) (f0 (r.SteamProduction * 3600.0)))
        kv sb "LMTD" (sprintf "%s K" (f1 r.LmtdMean))
        kv sb "U medio (rif. sup. esterna)" (sprintf "%s W/m2K" (f1 r.UMean))
        let a0 = List.head r.Axial
        let aL = List.last r.Axial
        let c0 = cells |> List.filter (fun x -> x.I = 0)
        let cl = cells |> List.filter (fun x -> x.I = c.NZ - 1)
        kv sb "h gas convettivo IN / OUT" (sprintf "%s / %s W/m2K"
                                               (f0 (c0 |> List.averageBy (fun x -> x.HConvGas)))
                                               (f0 (cl |> List.averageBy (fun x -> x.HConvGas))))
        kv sb "h gas radiativo IN / OUT" (sprintf "%s / %s W/m2K"
                                              (f1 (c0 |> List.averageBy (fun x -> x.HRadGas)))
                                              (f1 (cl |> List.averageBy (fun x -> x.HRadGas))))
        kv sb "Emissivita' gas IN / OUT" (sprintf "%s / %s"
                                              (f3 (c0 |> List.averageBy (fun x -> x.EpsGas)))
                                              (f3 (cl |> List.averageBy (fun x -> x.EpsGas))))
        kv sb "h ebollizione IN / OUT" (sprintf "%s / %s W/m2K"
                                            (f0 (c0 |> List.averageBy (fun x -> x.HBoil)))
                                            (f0 (cl |> List.averageBy (fun x -> x.HBoil))))
        kv sb "Velocita' gas IN / OUT" (sprintf "%s / %s m/s"
                                            (f1 (c0 |> List.averageBy (fun x -> x.VelGas)))
                                            (f1 (cl |> List.averageBy (fun x -> x.VelGas))))
        kv sb "Reynolds gas IN / OUT" (sprintf "%s / %s"
                                           (f0 (c0 |> List.averageBy (fun x -> x.ReGas)))
                                           (f0 (cl |> List.averageBy (fun x -> x.ReGas))))
        kv sb "Correlazione lato gas" (GasSide.correlationName c.Gas.Correlation)
        kv sb "Correlazione ebollizione" (WaterSide.poolBoilingName c.Water.Correlation)

        sb.AppendLine() |> ignore
        para sb "  " (sprintf "IN PAROLE SEMPLICI. Sono i risultati d'insieme. La POTENZA (%s MW) e' il calore che il gas cede all'acqua: e' il prodotto che l'apparecchio deve consegnare, e si ritrova tutto nel VAPORE prodotto (%s t/h). LMTD e' la differenza media di temperatura fra i due fluidi lungo tutto l'apparecchio: e' la 'spinta' che fa passare il calore. U e' il coefficiente globale di scambio: quanto calore passa per ogni metro quadrato e per ogni grado di differenza; e' l'inverso della somma di tutte le resistenze in serie. I coefficienti h sono le singole resistenze prese una a una: h del gas (il collo di bottiglia), h di irraggiamento (il calore che il gas emette come luce infrarossa, piccolo ma non nullo a 34 bar), h di ebollizione (enorme, quindi ininfluente). Reynolds dice se il gas e' in moto turbolento: sopra 10000 lo e' senz'altro, ed e' quello che si vuole perche' la turbolenza mescola e migliora lo scambio." (f2 (r.Duty / 1e6)) (f0 (r.SteamProduction * 3.6)))
        sb.AppendLine() |> ignore

        match r.BypassResult with
        | None -> ()
        | Some bp ->
            hdr sb "3b. By-pass interno centrale"
            kv sb "FRAZIONE DI PORTATA DEVIATA" (sprintf "%s %%  (%s kg/s su %s)" (f2 (100.0 * bp.Fraction)) (f2 bp.MassFlow) (f2 c.Gas.MassFlow))
            kv sb "T uscita dai TUBI (non miscelata)" (sprintf "%s °C" (f1 (kToC bp.TOutTubes)))
            kv sb "T uscita dal BY-PASS" (sprintf "%s °C" (f1 (kToC bp.TOutBypass)))
            kv sb "T USCITA MISCELATA" (sprintf "%s °C  (obiettivo %s °C)" (f1 (kToC bp.TOutMixed)) (f1 (kToC c.Bypass.TargetMixOut)))
            kv sb "Geometria liner / isolante / tubo"
                (sprintf "ID %s - OD %s | OD %s | OD %s mm"
                     (f1 (c.Bypass.LinerId * 1000.0)) (f1 (c.Bypass.LinerOd * 1000.0))
                     (f1 (c.Bypass.InsulOd * 1000.0)) (f1 (c.Bypass.PipeOd * 1000.0)))
            kv sb "Materiale liner / tubo" (sprintf "%s | %s" c.Bypass.LinerMaterial.Name c.Bypass.PipeMaterial.Name)
            kv sb "Velocita' gas nel by-pass IN / OUT"
                (sprintf "%s / %s m/s" (f1 (List.head bp.Nodes).Vel) (f1 (List.last bp.Nodes).Vel))
            kv sb "CALORE CEDUTO ATTRAVERSO L'ISOLANTE" (sprintf "%s kW  (%s %% della potenza totale)" (f0 (bp.HeatLoss / 1000.0)) (f2 (100.0 * bp.HeatLoss / r.Duty)))
            kv sb "  vapore generato dal by-pass" (sprintf "%s kg/s" (f2 bp.SteamFromBypass))
            kv sb "  raffreddamento del gas deviato" (sprintf "%s K" (f1 (c.Gas.TIn - bp.TOutBypass)))
            kv sb "T MAX LINER (faccia gas)" (sprintf "%s °C  su limite %s °C" (f1 (kToC bp.TLinerMax)) (f0 c.Bypass.LinerMaterial.TmaxDesign))
            kv sb "T MAX tubo di contenimento (faccia interna)" (sprintf "%s °C  su limite %s °C" (f1 (kToC bp.TPipeMax)) (f0 c.Bypass.PipeMaterial.TmaxDesign))
            kv sb "Salto termico sull'isolante (max)" (sprintf "%s K" (f1 (bp.Nodes |> List.map (fun n -> n.DTInsul) |> List.max)))
            kv sb "dP nel by-pass (tubo nudo)" (sprintf "%s mbar" (f1 (bp.DpBypass / 100.0)))
            kv sb "dP lato tubi" (sprintf "%s mbar" (f1 (r.DpGas / 100.0)))
            kv sb "dP DA STROZZARE sull'organo di regolazione"
                (sprintf "%s mbar" (f1 ((r.DpGas - bp.DpBypass) / 100.0)))
            sb.AppendLine() |> ignore
            sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
            sb.AppendLine("  " + String('-', 92)) |> ignore
            para sb "  " (sprintf "Nell'anima centrale del fascio corre un tubo che porta una parte del gas dall'ingresso all'uscita SENZA raffreddarla. Serve a regolare la temperatura di uscita: un WHB si dimensiona sporco, quindi da pulito raffredderebbe troppo, e il by-pass rialza la temperatura rimescolando gas caldo. Qui i tubi da soli porterebbero il gas a %s °C, il by-pass ne tiene %s%% a %s °C, e mescolando si ottengono i %s °C richiesti." (f1 (kToC bp.TOutTubes)) (f2 (100.0 * bp.Fraction)) (f1 (kToC bp.TOutBypass)) (f1 (kToC bp.TOutMixed)))
            para sb "  " (sprintf "Il tubo e' costruito a tre strati per la stessa ragione della ferrula: dentro un liner in %s che regge i quasi mille gradi, poi la carta ceramica che isola, poi il tubo di contenimento che porta la pressione e resta freddo. Il calcolo conferma il funzionamento: il liner sta a %s °C mentre il tubo di contenimento resta a %s °C, cioe' appena sopra la temperatura dell'acqua. Il salto sull'isolante e' di %s K: e' lui a fare tutto il lavoro." c.Bypass.LinerMaterial.Name (f0 (kToC bp.TLinerMax)) (f0 (kToC bp.TPipeMax)) (f0 (bp.Nodes |> List.map (fun n -> n.DTInsul) |> List.max)))
            para sb "  " (sprintf "L'isolamento non e' perfetto: attraverso la carta passano %s kW, cioe' il %s%% della potenza totale, che diventano %s kg/s di vapore in piu' e raffreddano il gas deviato di %s K. Non e' una perdita, e' calore comunque recuperato, ma va contato nel bilancio." (f0 (bp.HeatLoss / 1000.0)) (f2 (100.0 * bp.HeatLoss / r.Duty)) (f2 bp.SteamFromBypass) (f1 (c.Gas.TIn - bp.TOutBypass)))
            para sb "  " (sprintf "ATTENZIONE ALLA REGOLAZIONE. Il by-pass e i tubi collegano gli stessi due punti, quindi lavorano in parallelo: se il tubo di by-pass fosse libero, la sua bassa resistenza (%s mbar contro %s mbar del fascio) gli farebbe passare molto piu' gas del necessario e la temperatura d'uscita salirebbe fuori controllo. Serve quindi un organo di strozzamento che dissipi %s mbar. La frazione qui calcolata (%s%%) e' quella che centra l'obiettivo di temperatura, non quella che si otterrebbe a valvola spalancata." (f1 (bp.DpBypass / 100.0)) (f1 (r.DpGas / 100.0)) (f1 ((r.DpGas - bp.DpBypass) / 100.0)) (f2 (100.0 * bp.Fraction)))
            para sb "  " "EFFETTI SUL RESTO DEL CALCOLO. (1) Nei tubi passa meno gas, quindi il coefficiente di scambio e il flusso termico di picco calano un poco. (2) La potenza scambiata e la produzione di vapore calano, perche' parte del gas non cede calore. (3) Il tubo di by-pass occupa l'anima centrale del fascio: la sua sezione viene sottratta dall'area libera per l'acqua, e ne tiene conto il calcolo idraulico del mantello. (4) Il calore ceduto dall'isolante e' aggiunto alla produzione di vapore."
            sb.AppendLine("  " + String('-', 92)) |> ignore

        match r.Valve with
        | None -> ()
        | Some v ->
            hdr sb "3c. Valvola a farfalla del by-pass: ripartizione dei flussi"
            kv sb "Posizione della valvola"
                (sprintf "sul tratto %s del by-pass, DN interno %s mm"
                     (if v.AtOutlet then "di USCITA (estremita' fredda)" else "di INGRESSO (estremita' calda)")
                     (f0 (v.Diameter * 1000.0)))
            sb.AppendLine() |> ignore
            sb.AppendLine("  POSIZIONE IN ESERCIZIO NORMALE E LIMITI AMMESSI") |> ignore
            sb.AppendLine(line) |> ignore
            let vrow (lbl: string) (p: ValvePoint) =
                sb.AppendLine(
                    sprintf "  %-22s %6s° apertura (%5s° chiusura)  zeta = %8s  by-pass %5s %%  T misc %6s °C"
                        lbl (f1 p.OpenDeg) (f1 p.ClosureDeg) (f1 p.Zeta)
                        (f2 (100.0 * p.Fraction)) (f1 (kToC p.TMixed))) |> ignore
            vrow "APERTURA MINIMA" v.MinOpen
            vrow "ESERCIZIO NORMALE" v.Normal
            vrow "APERTURA MASSIMA" v.MaxOpen
            sb.AppendLine(line) |> ignore
            sb.AppendLine() |> ignore
            sb.AppendLine("  VINCOLI CHE DETERMINANO L'APERTURA MINIMA (vince il piu' alto)") |> ignore
            for (nm, a, why) in v.MinDrivers do
                sb.AppendLine(sprintf "    %-38s %6s°   %s" nm (f1 a) (if abs (a - v.MinOpen.OpenDeg) < 1e-6 then "<== VINCOLANTE" else "")) |> ignore
                para sb "        " why
            sb.AppendLine() |> ignore
            sb.AppendLine("  VINCOLI CHE DETERMINANO L'APERTURA MASSIMA (vince il piu' basso)") |> ignore
            for (nm, a, why) in v.MaxDrivers do
                sb.AppendLine(sprintf "    %-38s %6s°   %s" nm (f1 a) (if abs (a - v.MaxOpen.OpenDeg) < 1e-6 then "<== VINCOLANTE" else "")) |> ignore
                para sb "        " why
            sb.AppendLine() |> ignore
            sb.AppendLine("  COEFFICIENTE DI EFFLUSSO E CONFRONTO CON LA TEORIA DEL DISCO PIANO") |> ignore
            sb.AppendLine("  apert[°]  zeta tabella  zeta teoria  scarto[%]     Cv      Kv    Kv richiesto   x = dP/p1") |> ignore
            sb.AppendLine(line) |> ignore
            for p in v.Sweep do
                sb.AppendLine(
                    sprintf "  %7s %13s %12s %10s %7s %7s %14s %11s"
                        (f1 p.OpenDeg) (f1 p.Zeta) (f1 p.ZetaTheory)
                        (f1 (100.0 * (p.ZetaTheory / max 1e-6 p.Zeta - 1.0)))
                        (f0 p.Cv) (f0 p.Kv) (f0 p.KvRequired) (f4 p.XRatio)) |> ignore
            sb.AppendLine(line) |> ignore
            legend sb
                [ "zeta tabella", "Valore sperimentale di Idelchik per valvola a disco in condotto circolare, usato dal calcolo."
                  "zeta teoria", "Valore ricavato dalla sola geometria del disco piano concentrico: area libera sigma = 1 - sin(alpha) - 4t/(pi d) cos(alpha), contrazione nella vena con Cc di Weisbach = 0.62 + 0.38 sigma^3, riespansione di Borda-Carnot, piu' la resistenza di forma del disco a tutta apertura."
                  "Cv", "Coefficiente di efflusso americano: portata in gallon/min di acqua a 60 F con 1 psi di caduta. Si ricava da zeta con Cv = 29.9 d^2 / sqrt(zeta), d in pollici."
                  "Kv", "Equivalente metrico: m3/h di acqua con 1 bar di caduta. Kv = Cv / 1.156."
                  "Kv richiesto", "Kv che il servizio richiede a quella portata e a quella caduta: Kv = w / sqrt(1000 rho dp[bar]), w in kg/h. Deve coincidere con il Kv geometrico: e' una verifica incrociata del calcolo."
                  "x = dP/p1", "Rapporto di caduta. Sotto ~0.7 (limite x_T tipico di una farfalla) il flusso non e' critico e il gas si puo' trattare come incomprimibile: qui x vale pochi millesimi, quindi il modello incomprimibile e' abbondantemente valido." ]
            para sb "  " "PERCHE' DUE COLONNE DI ZETA. La curva del costruttore non e' disponibile, quindi si e' fatto quanto di meglio consente la teoria: si e' costruito zeta dalla sola geometria del disco piano concentrico. Il confronto con i dati sperimentali di Idelchik dice quanto vale quella teoria. A valvola tutta aperta i due valori coincidono (scarto pochi punti percentuali). Nel campo di lavoro, fra 20 e 40 gradi di apertura, la teoria e' CONSERVATIVA del 35-50 %, e lo scarto e' fisico: il passaggio reale attorno a un disco inclinato sono DUE LUCI A MEZZALUNA, con getti che si ricongiungono a valle e recuperano parte della pressione, mentre il modello tratta il passaggio come una contrazione unica seguita da riespansione brusca, senza recupero. Sotto i 10-15 gradi di apertura la teoria diverge del tutto: l'area libera geometrica tende a zero mentre nella valvola reale il passaggio e' governato dal trafilamento sulla battuta, che la geometria ideale non contiene."
            para sb "  " "COSA USARE. Per il dimensionamento si usa la TABELLA sperimentale, che e' il dato. La teoria serve a tre cose: verificare che la tabella sia coerente con la geometria reale del disco (lo e'), fornire una stima conservativa se il disco fosse di forma diversa da quello tabellato, e dare un limite superiore di zeta con cui controllare che la valvola sia comunque capace della caduta richiesta."
            para sb "  " "IL CONFRONTO Cv GEOMETRICO / Cv RICHIESTO e' la verifica piu' utile della sezione: il primo viene dalla forma della valvola, il secondo dal servizio che deve svolgere. Se coincidono, la valvola e' della taglia giusta all'apertura indicata."
            sb.AppendLine() |> ignore
            sb.AppendLine("  CARATTERISTICA COMPLETA: TEMPERATURE E PORTATE IN FUNZIONE DELL'ANGOLO") |> ignore
            sb.AppendLine("  apert[°] chius[°]     zeta   x[%]  w_bp[kg/s]  v_liner  v_vena  Mach  dPvalv[mbar]  T tubi[C]  T byp[C]  T MISC[C]  Q[MW]  vapore[t/h]  T liner[C]") |> ignore
            sb.AppendLine(line) |> ignore
            for p in v.Sweep do
                let mark =
                    if abs (p.OpenDeg - v.Normal.OpenDeg) < 0.05 then " <== NORMALE"
                    elif abs (p.OpenDeg - v.MinOpen.OpenDeg) < 0.05 then " <== MIN"
                    elif abs (p.OpenDeg - v.MaxOpen.OpenDeg) < 0.05 then " <== MAX"
                    else ""
                sb.AppendLine(
                    sprintf "  %7s %8s %8s %6s %11s %8s %7s %5s %13s %10s %9s %10s %6s %12s %11s%s"
                        (f1 p.OpenDeg) (f1 p.ClosureDeg) (f1 p.Zeta) (f2 (100.0 * p.Fraction))
                        (f2 p.MassFlowBypass) (f1 p.VelPipe) (f0 p.VelThroat) (f2 p.Mach)
                        (f1 (p.DpValve / 100.0)) (f1 (kToC p.TOutTubes)) (f0 (kToC p.TOutBypass))
                        (f1 (kToC p.TMixed)) (f1 (p.Duty / 1e6)) (f0 (p.Steam * 3.6)) (f0 (kToC p.TLinerMax)) mark) |> ignore
            sb.AppendLine(line) |> ignore
            legend sb
                [ "apert [°]", "Angolo di APERTURA della farfalla: 0° = disco perpendicolare al flusso (chiusa), 90° = disco parallelo (tutta aperta). E' la posizione dello stelo che si legge sull'attuatore."
                  "chius [°]", "Angolo di CHIUSURA = 90° - apertura. E' la variabile usata dalle tabelle di Idelchik, riportata per poter risalire alla fonte."
                  "zeta", "Coefficiente di perdita della valvola riferito alla velocita' media nel tubo: dP = zeta * 0.5 * rho * v^2. Varia di oltre tre ordini di grandezza fra tutta aperta e quasi chiusa."
                  "x [%]", "Frazione della portata totale di gas che passa nel by-pass. NON e' un dato: e' il risultato dell'uguaglianza delle perdite di carico fra i due rami in parallelo."
                  "v_liner", "Velocita' media del gas dentro il liner del by-pass."
                  "v_vena", "Velocita' stimata nella sezione ristretta della valvola (vena contratta), v = sqrt(2 dP / rho): e' quella che conta per erosione, rumore e vibrazione."
                  "Mach", "Rapporto fra la velocita' in vena contratta e la velocita' del suono nella miscela. Oltre 0.3 il rumore e le forzanti acustiche diventano un tema."
                  "dPvalv", "Salto di pressione dissipato dalla sola farfalla."
                  "T tubi", "Temperatura del gas all'uscita del FASCIO (non miscelata)."
                  "T byp", "Temperatura del gas all'uscita del BY-PASS."
                  "T MISC", "Temperatura del gas DOPO la miscelazione dei due flussi: e' il valore che il processo richiede e che la valvola regola."
                  "Q [MW]", "Potenza termica complessivamente scambiata (fascio + perdita attraverso l'isolante del by-pass)."
                  "T liner", "Temperatura massima della faccia calda del liner: sale aprendo, perche' il gas attraversa piu' in fretta e si raffredda meno." ]
            sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
            sb.AppendLine("  " + String('-', 92)) |> ignore
            para sb "  " "COME SI RIPARTISCE IL GAS. Il fascio tubiero e il tubo di by-pass collegano gli stessi due punti: la camera d'ingresso e la camera d'uscita. Sono quindi due resistenze in parallelo, e vale la regola dei circuiti: la portata si divide in modo che i due rami dissipino LA STESSA caduta di pressione. Non e' quindi il progettista a decidere quanto gas devia: decide solo la resistenza della valvola, e la fisica decide il resto."
            para sb "  " (sprintf "PERCHE' SERVE LA VALVOLA. Il ramo di by-pass e' un tubo liscio da %s mm con dentro pochissima portata: da solo dissipa appena %s mbar, contro i %s mbar del fascio. Se fosse libero prenderebbe una frazione enorme del gas e la temperatura di uscita andrebbe fuori controllo. La farfalla serve proprio a bruciare la differenza: in esercizio normale deve dissipare %s mbar, che corrispondono a un coefficiente di perdita zeta = %s, cioe' a un'apertura di %s gradi." (f0 (v.Diameter * 1000.0)) (f2 ((v.Normal.DpBypassTot - v.Normal.DpValve) / 100.0)) (f1 (v.Normal.DpTubes / 100.0)) (f1 (v.Normal.DpValve / 100.0)) (f0 v.Normal.Zeta) (f1 v.Normal.OpenDeg))
            para sb "  " (sprintf "COSA SUCCEDE APRENDO. Aprendo la farfalla passa piu' gas caldo nel by-pass: la temperatura miscelata SALE, la potenza scambiata e il vapore CALANO, e il liner si scalda (meno tempo per raffreddarsi). Chiudendo succede il contrario. Fra apertura minima e massima ammesse la temperatura miscelata si muove fra %s e %s °C, cioe' circa %s K per grado di stelo: e' la sensibilita' che il regolatore deve gestire." (f1 (kToC v.MinOpen.TMixed)) (f1 (kToC v.MaxOpen.TMixed)) (f2 (abs (v.MaxOpen.TMixed - v.MinOpen.TMixed) / max 1.0 (v.MaxOpen.OpenDeg - v.MinOpen.OpenDeg))))
            para sb "  " "PERCHE' ESISTE UN'APERTURA MINIMA E UNA MASSIMA. Non e' una questione di sola temperatura. Chiudendo troppo: (a) la miscelata scende sotto il minimo di processo; (b) nel liner resta cosi' poca portata da farne un ramo morto, dove il gas stratifica e si deposita sporco; (c) tutta la caduta si concentra nella fessura del disco, dove la velocita' e il rumore crescono; (d) sotto una quindicina di gradi la valvola diventa di fatto un interruttore, perche' zeta cambia del 30% per ogni grado di stelo. Aprendo troppo: (e) la miscelata supera il massimo di processo; (f) il liner si avvicina al suo limite metallurgico; (g) oltre una settantina di gradi la valvola non ha piu' autorita', perche' zeta e' ormai piatto e muovere lo stelo non cambia nulla."
            para sb "  " "POSIZIONE DI SICUREZZA. La valvola deve essere FAIL-CLOSED: in mancanza di aria strumenti o di segnale si chiude, tutta la portata va al fascio, la temperatura di uscita scende e il vapore aumenta. E' la condizione piu' sicura sia per l'apparecchiatura a valle sia per il liner. Il rischio da evitare e' l'opposto: una valvola che si apre in avaria manderebbe gas a quasi mille gradi direttamente all'uscita."
            para sb "  " "CORRELAZIONI USATE. (1) Coefficiente di perdita della farfalla: tabella di Idelchik per valvola a disco in condotto circolare, in funzione dell'angolo di chiusura, interpolata in scala logaritmica (zeta varia in modo esponenziale con l'angolo). (2) Perdita distribuita nel liner e nei tubi: Darcy-Weisbach con fattore d'attrito di Colebrook/Filonenko. (3) Perdite localizzate di imbocco e sbocco: coefficienti classici 0.5 e 1.0. (4) Ripartizione: uguaglianza delle cadute fra rami in parallelo, risolta per bisezione. (5) Miscelazione: bilancio ENTALPICO (non di temperatura), con entalpie assolute che includono i calori di formazione, cosi' che l'eventuale avanzamento della reazione di shift sia contabilizzato correttamente."
            sb.AppendLine("  " + String('-', 92)) |> ignore

        hdr sb "4. Effetto fascio tubiero (dispersione fra le bande)"
        sb.AppendLine("  banda    y[m]   n.tubi   T gas out[C]   q''max[kW/m2]  Tmet.int max[C]  x uscita  alpha  DNBR min") |> ignore
        sb.AppendLine(line) |> ignore
        for j in 0 .. ny - 1 do
            let cj = cells |> List.filter (fun x -> x.J = j)
            let last = cj |> List.maxBy (fun x -> x.I)
            let b = r.Bands.[j]
            sb.AppendLine(
                sprintf "  %5d %7s %8s %14s %14s %16s %9s %6s %9s"
                    j (f3 b.Y) (f0 b.NTubes) (f1 (kToC last.TGas))
                    (f1 ((cj |> List.filter (fun x -> not x.InFerrule) |> List.map (fun x -> x.QFluxOut) |> List.max) / 1000.0))
                    (f1 (kToC (cj |> List.map (fun x -> x.TMetalIn) |> List.max)))
                    (f4 (cj |> List.map (fun x -> x.XOut) |> List.max))
                    (f3 (cj |> List.map (fun x -> x.Alpha) |> List.max))
                    (f2 (cj |> List.filter (fun x -> not x.InFerrule) |> List.map (fun x -> x.DNBR) |> List.min))) |> ignore
        sb.AppendLine(line) |> ignore
        legend sb
            [ "banda",
              "Indice della fascia orizzontale in cui e' stato suddiviso il fascio tubiero. La banda 0 e' la piu' BASSA, l'ultima e' la piu' ALTA. L'acqua attraversa il fascio dal basso verso l'alto, quindi le bande sono percorse in serie: quello che esce dalla banda j entra nella banda j+1."
              "y [m]",
              "Quota del centro della banda misurata dall'asse del mantello: negativa sotto l'asse, positiva sopra. La banda piu' bassa e quella piu' alta hanno |y| massimo e contengono pochi tubi (il fascio e' circolare, li' e' stretto)."
              "n. tubi",
              "Numero di tubi contenuti nella banda, ricavato dall'area intubata della fascia divisa per l'area di competenza di un tubo (passo^2 * sin60). La somma sulle bande restituisce il numero totale di tubi."
              "T gas out [C]",
              "Temperatura del gas di processo all'USCITA dei tubi di quella banda. I tubi sono canali in parallelo, quindi ogni banda ha il suo profilo: la dispersione fra bande e' l'effetto fascio visto dal lato gas."
              "q'' max [kW/m2]",
              "Massimo flusso termico specifico raggiunto lungo la banda, riferito alla superficie ESTERNA del tubo (quella bagnata dall'acqua). Il tratto protetto dalla ferrula e' escluso, perche' li' il flusso e' artificialmente basso e non e' rappresentativo."
              "Tmet.int max [C]",
              "Massima temperatura del METALLO sulla superficie INTERNA del tubo (lato gas), che e' il punto piu' caldo della parete. Va confrontata con il limite di progetto del materiale."
              "x uscita",
              "TITOLO massico della miscela che esce dalla banda: rapporto fra la portata di vapore e la portata totale (vapore + acqua). Cresce salendo perche' ogni banda aggiunge il vapore che ha prodotto. Adimensionale: 0.10 = 10% in massa e' vapore."
              "alpha",
              "FRAZIONE DI VUOTO: rapporto fra l'area occupata dal vapore e l'area totale della sezione. E' molto piu' grande del titolo perche' il vapore e' meno denso: a 118 bar x = 0.10 corrisponde gia' ad alpha = 0.45. E' alpha, non x, a dire se i ranghi alti restano bagnati (limite pratico 0.7)."
              "DNBR min",
              "Minimo rapporto locale fra il flusso termico critico (CHF, quello che innescherebbe il film di vapore) e il flusso termico effettivo. E' un margine: 3 = si lavora a un terzo del limite, 1 = si e' al limite, sotto 1 = criterio violato. Peggiora salendo perche' il CHF cala al crescere del titolo locale." ]
        sb.AppendLine("  COME SI LEGGE: la banda 0 riceve acqua satura (x = 0) e lavora nelle condizioni piu'") |> ignore
        sb.AppendLine("  favorevoli. Salendo, ogni banda riceve la miscela gia' arricchita da tutte quelle") |> ignore
        sb.AppendLine("  sottostanti, quindi titolo e frazione di vuoto crescono e il margine sul DNB cala.") |> ignore
        sb.AppendLine("  La temperatura del gas in uscita cambia invece pochissimo fra le bande: la resistenza") |> ignore
        sb.AppendLine("  lato acqua e' una frazione minima di quella totale, quindi anche dimezzando il") |> ignore
        sb.AppendLine("  coefficiente di ebollizione il bilancio termico si sposta di poco. L'effetto fascio") |> ignore
        sb.AppendLine("  NON si vede sul gas: si vede sul margine di DNB della banda superiore.") |> ignore

        hdr sb "5. Flusso termico e temperature metalliche"
        let qmax = hot |> List.maxBy (fun x -> x.QFluxOut)
        let tmax = cells |> List.maxBy (fun x -> x.TMetalIn)
        let dnb = hot |> List.minBy (fun x -> x.DNBR)
        kv sb "Flusso termico massimo (est.)" (sprintf "%s kW/m2  @ z = %s m, y = %s m (banda %d)"
                                                   (f1 (qmax.QFluxOut / 1000.0)) (f2 qmax.Z) (f3 qmax.Y) qmax.J)
        kv sb "Flusso termico massimo (int.)" (sprintf "%s kW/m2" (f1 (qmax.QFluxIn / 1000.0)))
        kv sb "Flusso termico medio" (sprintf "%s kW/m2" (f1 (r.Duty / r.AreaOut / 1000.0)))
        kv sb "T metallo max (int / media / est)"
            (sprintf "%s / %s / %s °C  @ z = %s m, y = %s m"
                 (f1 (kToC tmax.TMetalIn)) (f1 (kToC tmax.TMetalMid)) (f1 (kToC tmax.TMetalOut))
                 (f2 tmax.Z) (f3 tmax.Y))
        kv sb "Surriscaldamento reale di parete max" (sprintf "%s K" (f2 (hot |> List.map (fun x -> x.DTsatWall) |> List.max)))
        kv sb "Salto attraverso il deposito (max)" (sprintf "%s K" (f1 (hot |> List.map (fun x -> x.DTDeposit) |> List.max)))
        kv sb "T metallo esterna - Tsat (max)" (sprintf "%s K" (f1 (hot |> List.map (fun x -> x.DTMetalSat) |> List.max)))
        let qCritTube = min (WaterSide.chfHorizontalTube c.Tube.Do r.Sat) (WaterSide.chfMostinski r.Sat.P Pc_water)
        kv sb "CHF tubo singolo (pool)" (sprintf "%s kW/m2" (f0 (qCritTube / 1000.0)))
        kv sb "phi_b di Palen" (f3 (WaterSide.palenPhiB c.Tube.Otl c.Tube.Length r.AreaOut))
        kv sb "dT critico (ginocchio curva ebollizione)"
            (sprintf "%s K" (f1 (WaterSide.dTcrit c.Water.Correlation qCritTube c.Tube.Do r.Sat c.Water.RoughnessUm c.Water.Csf)))
        kv sb "DNBR locale minimo" (sprintf "%s  @ z = %s m, y = %s m, x = %s"
                                        (f2 dnb.DNBR) (f2 dnb.Z) (f3 dnb.Y) (f4 dnb.XOut))

        let tGasPk = kToC qmax.TGas
        let tMiPk = kToC qmax.TMetalIn
        let tMoPk = kToC qmax.TMetalOut
        let tWbPk = kToC qmax.TWallBoil
        let tSat = kToC r.Sat.Tsat
        let dGas = tGasPk - tMiPk
        let dWall = tMiPk - tMoPk
        let dDep = tMoPk - tWbPk
        let dBoil = tWbPk - tSat
        let dTot = tGasPk - tSat
        let pc (x: float) = 100.0 * x / dTot
        sb.AppendLine() |> ignore
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " (sprintf "Tutti i numeri di questo riquadro sono presi nel PUNTO DI FLUSSO MASSIMO (z = %s m, y = %s m, banda %d), cosi' la catena torna. Le righe della tabella qui sopra sono invece massimi presi su tutto l'apparecchio, quindi possono cadere in celle leggermente diverse fra loro." (f2 qmax.Z) (f3 qmax.Y) qmax.J)
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "Il calore deve andare dal gas (%s °C) all'acqua che bolle (%s °C) attraversando quattro ostacoli in fila, come una serie di strozzature su un tubo. Ogni ostacolo si 'paga' con un salto di temperatura, e la somma dei quattro salti fa la differenza totale di %s °C:" (f0 tGasPk) (f1 tSat) (f0 dTot))
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "    1) film di gas + sporco lato gas ......  %6s K   (%s%% del totale)" (f0 dGas) (f0 (pc dGas))) |> ignore
        sb.AppendLine(sprintf "    2) spessore del metallo ..............  %6s K   (%s%%)" (f0 dWall) (f0 (pc dWall))) |> ignore
        sb.AppendLine(sprintf "    3) deposito lato acqua ...............  %6s K   (%s%%)" (f0 dDep) (f0 (pc dDep))) |> ignore
        sb.AppendLine(sprintf "    4) film di ebollizione ...............  %6s K   (%s%%)" (f0 dBoil) (f0 (pc dBoil))) |> ignore
        sb.AppendLine() |> ignore
        para sb "  " "Il primo ostacolo e' di gran lunga il piu' grande: e' il gas a fare da collo di bottiglia, ed e' per questo che la temperatura del metallo resta comunque vicina a quella dell'acqua e non a quella del gas. Se cosi' non fosse, un tubo di acciaio non potrebbe stare in un gas a quasi 1000 gradi."
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "FLUSSO TERMICO (i primi tre numeri). E' quanti watt passano attraverso ogni metro quadrato di tubo: la 'densita' di potenza' della parete. Compare due volte perche' la stessa potenza attraversa una superficie interna piu' piccola (diametro %s mm) e una esterna piu' grande (%s mm): stesso calore, area diversa, quindi numeri diversi nel rapporto %s. Per convenzione il valore di riferimento e' quello ESTERNO, perche' e' la faccia bagnata dall'acqua ed e' li' che si decide se l'ebollizione tiene." (f1 (c.Tube.Di * 1000.0)) (f1 (c.Tube.Do * 1000.0)) (f2 (c.Tube.Do / c.Tube.Di)))
        para sb "  " (sprintf "Il massimo (%s kW/m2) e' %s volte il valore medio (%s kW/m2). Non e' un difetto: il gas entra caldissimo e si raffredda lungo il tubo, quindi lo scambio e' concentrato nei primi metri. Il picco cade a z = %s m, cioe' subito a valle dell'estremita' della ferrula: sotto la ferrula il tubo e' protetto dall'isolante, appena finisce il metallo si trova esposto al gas ancora quasi alla temperatura d'ingresso. E' il punto piu' sollecitato dell'apparecchio." (f0 (qmax.QFluxOut / 1000.0)) (f1 (qmax.QFluxOut / (r.Duty / r.AreaOut))) (f0 (r.Duty / r.AreaOut / 1000.0)) (f2 qmax.Z))
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "TEMPERATURE DEL METALLO (tre valori). Sono la stessa parete letta in tre punti dello spessore: faccia interna a contatto col gas (%s °C nel punto di flusso massimo, la piu' calda), meta' spessore (%s °C) e faccia esterna a contatto con l'acqua (%s °C). I %s gradi fra le due facce sono il prezzo per far passare il calore attraverso %s mm di acciaio. Il valore da confrontare con il limite del materiale (%s, %s °C) e' quello INTERNO; il massimo su tutto l'apparecchio, riportato nella tabella, e' %s °C." (f0 tMiPk) (f0 (kToC qmax.TMetalMid)) (f0 tMoPk) (f0 dWall) (f2 ((c.Tube.Do - c.Tube.Di) * 500.0)) c.Material.Name (f0 c.Material.TmaxDesign) (f0 (kToC tmax.TMetalIn)))
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "SURRISCALDAMENTO DI PARETE (%s K) e SALTO NEL DEPOSITO (%s K). Sono due cose diverse che spesso vengono confuse. Il surriscaldamento e' di quanto la superficie realmente bagnata dall'acqua e' piu' calda dell'acqua stessa: e' la spinta che fa nascere le bolle, e va tenuta sotto il 'dT critico'. Il salto nel deposito e' invece quanto il velo di ossidi che si forma sul tubo isola il metallo dall'acqua. Nel punto di flusso massimo il deposito vale %s K contro %s K di surriscaldamento: cioe' il metallo e' caldo per lo sporco, non per l'ebollizione. E' un punto pratico importante, perche' significa che la qualita' dell'acqua di caldaia pesa piu' del progetto termico, e che il problema peggiora da solo (piu' caldo -> piu' deposito -> piu' caldo)." (f1 (hot |> List.map (fun x -> x.DTsatWall) |> List.max)) (f1 (hot |> List.map (fun x -> x.DTDeposit) |> List.max)) (f0 dDep) (f0 dBoil))
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "dT CRITICO (%s K). E' il 'ginocchio' della curva di ebollizione. Fino a li' le bolle si staccano una a una e portano via calore benissimo. Oltre, le bolle si fondono in un velo di vapore continuo che fa da coperta isolante: lo scambio crolla e il metallo si scalda di centinaia di gradi in pochi minuti. Il surriscaldamento calcolato (%s K) supera questo valore, ed e' il motivo dell'allarme in diagnostica." (f1 (WaterSide.dTcrit c.Water.Correlation qCritTube c.Tube.Do r.Sat c.Water.RoughnessUm c.Water.Csf)) (f1 (hot |> List.map (fun x -> x.DTsatWall) |> List.max)))
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "CHF e phi_b DI PALEN. Il CHF (%s kW/m2) e' il flusso termico che farebbe scattare quel velo di vapore su un tubo SINGOLO immerso in acqua ferma. In un fascio fitto le cose vanno peggio, perche' il vapore prodotto dai tubi sotto deve passare fra i tubi sopra: il fattore phi_b di Palen tiene conto di questo affollamento e qui vale %s, cioe' abbatte il limite a %s kW/m2. Il criterio e' tarato su ribollitori kettle ed e' conservativo per un fascio attraversato da acqua in movimento come il nostro, ma resta l'indicatore di riferimento." (f0 (qCritTube / 1000.0)) (f2 (WaterSide.palenPhiB c.Tube.Otl c.Tube.Length r.AreaOut)) (f0 (qCritTube * WaterSide.palenPhiB c.Tube.Otl c.Tube.Length r.AreaOut / 1000.0)))
        sb.AppendLine() |> ignore
        para sb "  " (sprintf "DNBR LOCALE MINIMO (%s). E' il voto finale di questa sezione: quante volte il flusso termico effettivo sta dentro il limite. Sopra 2-3 si e' tranquilli, 1 e' il limite esatto, sotto 1 il criterio e' violato. Il minimo NON cade dove il flusso e' massimo (z = %s m) ma a z = %s m, y = %s m: li' il flusso e' un po' minore, ma l'acqua che lava il tubo ha gia' attraversato tutte le file sottostanti e contiene il %s%% di vapore in massa, quindi il limite si e' abbassato piu' di quanto sia sceso il flusso. E' esattamente il meccanismo dello steam blanketing: non serve un flusso enorme, basta che l'acqua che arriva sia gia' troppo carica di vapore." (f2 dnb.DNBR) (f2 qmax.Z) (f2 dnb.Z) (f3 dnb.Y) (f1 (100.0 * dnb.XOut)))
        sb.AppendLine("  " + String('-', 92)) |> ignore

        if List.length r.FerruleClasses > 1 then
            hdr sb "5b. Classi di lunghezza della ferrula"
            sb.AppendLine("  classe  frazione  L[mm]   q''max[kW/m2]  z picco[m]  Tmet.int max[C]  DNBR min  T gas out[C]  quota duty[%]") |> ignore
            sb.AppendLine(line) |> ignore
            for fc in r.FerruleClasses do
                sb.AppendLine(
                    sprintf "  %6d %9s %6s %14s %11s %16s %9s %13s %14s"
                        fc.Index (f2 fc.Frac) (f0 (fc.Length * 1000.0))
                        (f1 (fc.QFluxMax / 1000.0)) (f2 fc.ZQMax)
                        (f1 (kToC fc.TMetalInMax)) (f2 fc.DNBRMin)
                        (f1 (kToC fc.TGasOut)) (f1 (100.0 * fc.Duty / r.Duty))) |> ignore
            sb.AppendLine(line) |> ignore
            legend sb
                [ "classe", "Indice della popolazione di tubi con la stessa lunghezza di ferrula."
                  "frazione", "Quota dei tubi totali che appartiene alla classe (la somma fa 1)."
                  "L [mm]", "Lunghezza della ferrula misurata dalla faccia interna della piastra tubiera."
                  "q'' max [kW/m2]", "Massimo flusso termico della classe, riferito alla superficie esterna, escluso il tratto sotto ferrula."
                  "z picco [m]", "Ascissa a cui cade il picco di flusso. Cade sempre subito a VALLE dell'estremita' della ferrula: allungando la ferrula il picco si sposta dove il gas si e' gia' raffreddato."
                  "Tmet.int max [C]", "Massima temperatura del metallo sulla superficie interna per quella classe."
                  "DNBR min", "Minimo margine su DNB della classe."
                  "T gas out [C]", "Temperatura media del gas in uscita dai tubi della classe."
                  "quota duty [%]", "Percentuale della potenza totale scambiata dai tubi della classe (approssimativamente pari alla frazione di tubi)." ]
            sb.AppendLine("  Il progetto e' dettato dalla classe con la ferrula piu' corta, non dalla media:") |> ignore
            sb.AppendLine("  il tubo peggiore e' quello che cede per primo.") |> ignore

        hdr sb "5c. Pulito / sporco sui due lati (cella di flusso massimo)"
        sb.AppendLine("  condizione                              Rf gas    Rf acqua      U      q''      T met.int  T met.est  dT dep.  DNBR") |> ignore
        sb.AppendLine("                                        [m2K/W]    [m2K/W] [W/m2K] [kW/m2]      [°C]       [°C]     [K]") |> ignore
        sb.AppendLine(line) |> ignore
        for fc in r.FoulingCases do
            sb.AppendLine(
                sprintf "  %-38s %8s %10s %7s %8s %10s %10s %8s %5s"
                    fc.Label (f5 fc.RfIn) (f5 fc.RfOut) (f0 fc.U) (f1 (fc.QFlux / 1000.0))
                    (f1 (kToC fc.TMetalIn)) (f1 (kToC fc.TMetalOut)) (f1 fc.DTDeposit) (f2 fc.DNBR)) |> ignore
        sb.AppendLine(line) |> ignore
        (let cl = r.FoulingCases |> List.find (fun x -> x.RfIn = 0.0 && x.RfOut = 0.0)
         let di = r.FoulingCases |> List.maxBy (fun x -> x.RfIn + x.RfOut)
         kv sb "SCARTO pulito -> sporco su U" (sprintf "%s %%" (f1 (100.0 * (di.U / cl.U - 1.0))))
         kv sb "SCARTO pulito -> sporco sul flusso di picco" (sprintf "%s %%" (f1 (100.0 * (di.QFlux / cl.QFlux - 1.0))))
         kv sb "SCARTO pulito -> sporco su T metallo interna" (sprintf "%s K" (f1 (di.TMetalIn - cl.TMetalIn))))
        legend sb
            [ "Rf gas", "Resistenza di sporcamento sulla superficie INTERNA dei tubi (lato gas di processo): fuliggine, carbone da metal dusting, particolato di catalizzatore."
              "Rf acqua", "Resistenza di sporcamento sulla superficie ESTERNA (lato acqua): ossidi di ferro e magnetite depositati dall'acqua di caldaia."
              "U [W/m2K]", "Coefficiente globale riferito alla superficie esterna, U = q''/(T_gas - Tsat)."
              "q'' [kW/m2]", "Flusso termico locale sulla superficie esterna in quella condizione."
              "T met. int/est", "Temperature del metallo sulle due facce."
              "dT dep. [K]", "Salto di temperatura sul solo deposito lato acqua: e' quello che alza la temperatura del metallo senza dare scambio."
              "DNBR", "Margine su crisi di ebollizione con il CHF locale gia' calcolato." ]
        para sb "  " "IN PAROLE SEMPLICI. Il progetto e' fatto in condizione SPORCA, come da datasheet: e' la condizione di fine campagna, quella che garantisce la potenza anche quando l'apparecchio si e' incrostato. Ma le due condizioni vanno guardate insieme, perche' mettono in evidenza due rischi opposti."
        para sb "  " "DA PULITO il coefficiente globale e' piu' alto, quindi il flusso termico di PICCO e' maggiore e il margine su crisi di ebollizione e' PEGGIORE. La condizione critica per il DNB e' quindi l'apparecchio APPENA MESSO IN SERVIZIO, non quello sporco. E' un punto che si perde facilmente, perche' istintivamente si associa il rischio allo sporco."
        para sb "  " "DA SPORCO il flusso cala, ma la temperatura del metallo SALE, perche' il deposito lato acqua e' una coperta termica: il calore deve comunque passare e lo fa con un salto in piu'. Il rischio da sporco non e' la crisi di ebollizione, e' il surriscaldamento e lo scorrimento viscoso del metallo."
        para sb "  " "I DUE LATI NON SONO EQUIVALENTI. Lo sporco lato GAS aggiunge resistenza a MONTE del metallo: riduce il flusso e RAFFREDDA il tubo, quindi e' quasi benefico dal punto di vista metallurgico. Lo sporco lato ACQUA aggiunge resistenza a VALLE: riduce il flusso ma SCALDA il tubo. E' per questo che il condizionamento chimico dell'acqua conta piu' della pulizia lato gas."
        para sb "  " "Attenzione: questo confronto e' LOCALE, a temperatura del gas congelata al valore della cella di picco. Il confronto completo sull'intero apparecchio - dove da pulito il gas si raffredda di piu' e cambia tutto il profilo - si ottiene rilanciando il calcolo con le resistenze azzerate."

        hdr sb "5d. Modelli di flusso termico critico a confronto"
        let dnbCell = r.Cells |> List.filter (fun x -> not x.InFerrule) |> List.minBy (fun x -> x.DNBR)
        kv sb "Cella governante"
            (sprintf "z = %s m, y = %+.2f m (banda %d), titolo locale %s, velocita' miscela %s m/s"
                 (f2 dnbCell.Z) dnbCell.Y dnbCell.J (f3 dnbCell.XOut) (f2 dnbCell.VelCross))
        kv sb "Flusso termico locale" (sprintf "%s kW/m2" (f1 (dnbCell.QFluxOut / 1000.0)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  modello                                                              q crit[kW/m2]   DNBR") |> ignore
        sb.AppendLine(line) |> ignore
        for m in r.ChfModels do
            sb.AppendLine(sprintf "  %-66s %13s %6s"
                              (if m.Model.Length > 66 then m.Model.Substring(0, 66) else m.Model)
                              (f0 (m.QCrit / 1000.0)) (f2 m.DNBR)) |> ignore
        sb.AppendLine(line) |> ignore
        for m in r.ChfModels do
            para sb "    " (sprintf "%s: %s" m.Model m.Note)
        legend sb
            [ "q crit", "Flusso termico critico previsto dal modello nelle condizioni locali (titolo, velocita', pressione)."
              "DNBR", "Rapporto fra il flusso critico e il flusso effettivo. E' un margine: 2 = si lavora a meta' del limite, 1 = al limite." ]
        para sb "  " "IN PAROLE SEMPLICI. Il flusso termico critico e' il valore oltre il quale il vapore forma un film continuo sulla parete e lo scambio crolla: la parete si scalda di colpo di centinaia di gradi. Non esiste UNA formula: esistono correlazioni tarate su geometrie diverse, e la scelta cambia la risposta di un fattore anche superiore a due. Per questo si riportano tutte."
        para sb "  " "IL CRITERIO DI PALEN, che il calcolo usa come riferimento, e' tarato sui RIBOLLITORI KETTLE: fasci immersi in un bagno fermo, dove l'unica circolazione e' quella indotta dalle bolle. Il suo fattore di fascio penalizza pesantemente i fasci grandi, ed e' giusto che lo faccia in quel contesto. Qui pero' l'acqua attraversa il fascio spinta dalla circolazione naturale, con velocita' misurabili: la situazione e' molto meno severa di un kettle."
        para sb "  " "IL CRITERIO DI LIENHARD-EICHHORN e' quello geometricamente corretto per questo apparecchio: cilindro investito da una corrente. Tiene conto esplicitamente della velocita' della miscela, che nei criteri di tipo kettle non compare affatto. Il derating sul titolo tiene conto del fatto che, salendo nel fascio, c'e' sempre meno liquido disponibile a bagnare la parete."
        para sb "  " "LA CONCLUSIONE ONESTA DI QUESTA SEZIONE. I modelli non divergono di poco: divergono di un ORDINE DI GRANDEZZA, e non perche' uno sia sbagliato, ma perche' NESSUNO di essi e' tarato su questa geometria a questa pressione. Palen e' usato con il fattore di fascio troncato al minimo ammesso, cioe' fuori dal suo campo; Zuber e Lienhard-Dhir ignorano del tutto l'effetto fascio; Lienhard-Eichhorn e' fuori campo sul rapporto di densita'. Il DNBR calcolato NON e' quindi un numero affidabile in valore assoluto: e' un indicatore, e come tale va usato solo per confrontare zone diverse dello stesso apparecchio o varianti dello stesso progetto."
        para sb "  " "COSA RESTA AFFIDABILE. Il FLUSSO TERMICO DI PICCO, che il calcolo determina senza bisogno di alcuna correlazione di crisi, e che si confronta con l'esperienza di apparecchi analoghi in servizio. E la POSIZIONE del punto critico, che e' robusta rispetto a tutte le assunzioni: subito a valle della ferrula, nella banda superiore. Se serve una risposta quantitativa sul margine di crisi, la strada e' una sola: dati sperimentali su fascio, oppure la look-up table di Groeneveld con i fattori correttivi per fascio, che richiede l'accesso alla tabella originale."

        hdr sb "5e. Incertezza dovuta alla scelta delle correlazioni"
        kv sb "Valutata nella cella" (sprintf "z = %s m, y = %+.2f m (flusso massimo)" (f2 (r.Cells |> List.filter (fun x -> not x.InFerrule) |> List.maxBy (fun x -> x.QFluxOut)).Z) (r.Cells |> List.filter (fun x -> not x.InFerrule) |> List.maxBy (fun x -> x.QFluxOut)).Y)
        sb.AppendLine() |> ignore
        sb.AppendLine("  gruppo                     correlazione                                 h gas    h boil       U     q''    T met  scarto") |> ignore
        sb.AppendLine("                                                                        [W/m2K]  [W/m2K] [W/m2K] [kW/m2]   [°C]     [%]") |> ignore
        sb.AppendLine(line) |> ignore
        for it in r.Sensitivity do
            sb.AppendLine(
                sprintf "  %-26s %-40s %8s %8s %7s %7s %6s %7s"
                    it.Group
                    (if it.Name.Length > 40 then it.Name.Substring(0, 40) else it.Name)
                    (f0 it.HGas) (f0 it.HBoil) (f0 it.U) (f1 (it.QFlux / 1000.0))
                    (f0 (kToC it.TMetalIn)) (f2 it.Delta)) |> ignore
        sb.AppendLine(line) |> ignore
        (let byG =
            r.Sensitivity
            |> List.groupBy (fun x -> x.Group)
            |> List.map (fun (g, xs) ->
                (g, (xs |> List.map (fun x -> x.Delta) |> List.min),
                    (xs |> List.map (fun x -> x.Delta) |> List.max),
                    (xs |> List.map (fun x -> kToC x.TMetalIn) |> List.min),
                    (xs |> List.map (fun x -> kToC x.TMetalIn) |> List.max)))
         sb.AppendLine("  BANDA DI INCERTEZZA PER GRUPPO") |> ignore
         for (g, dmin, dmax, tmin2, tmax2) in byG do
            sb.AppendLine(sprintf "    %-28s flusso da %s %% a %s %%   |   T metallo da %s a %s °C"
                              g (f1 dmin) (f1 dmax) (f0 tmin2) (f0 tmax2)) |> ignore)
        legend sb
            [ "h gas", "Coefficiente di scambio lato gas che quella correlazione produce nelle condizioni locali, incluso l'irraggiamento e le correzioni di proprieta' variabili e d'imbocco."
              "h boil", "Coefficiente di ebollizione con il fattore di fascio applicato."
              "U", "Coefficiente globale risultante, riferito alla superficie esterna."
              "scarto [%]", "Differenza percentuale del flusso termico rispetto alla combinazione scelta come base del calcolo." ]
        para sb "  " "IN PAROLE SEMPLICI. Tutte le correlazioni di scambio termico sono adattamenti di dati sperimentali, e ognuna e' stata ricavata su un campo di prove diverso. Applicate allo stesso caso danno risposte che differiscono, e la differenza NON e' un errore: e' l'incertezza intrinseca del metodo. Questa tabella la misura invece di nasconderla."
        para sb "  " "COSA GUARDARE. Se la banda sul flusso e' stretta, il risultato e' robusto e si puo' procedere. Se e' larga, la grandezza in questione va trattata come incerta, e le decisioni di progetto non devono dipendere da quale correlazione si e' scelta. La banda sulla TEMPERATURA DEL METALLO e' quella che conta di piu', perche' e' il numero che si confronta con i limiti metallurgici."
        para sb "  " "PERCHE' LA RESISTENZA LATO GAS DOMINA. Si nota subito che cambiare la correlazione di ebollizione sposta pochissimo: e' perche' la resistenza lato acqua e' una frazione minima del totale. Il collo di bottiglia e' il film di gas, quindi l'incertezza del risultato e' quasi tutta l'incertezza della correlazione lato gas. E' anche il motivo per cui si e' scelto Gnielinski, che e' la piu' accurata nel campo di transizione e turbolento."
        para sb "  " "LA REGOLA DI MISCELAZIONE non e' un dettaglio: per una miscela ricca di idrogeno la conducibilita' calcolata con la media molare e quella calcolata con Wassiljewa-Mason-Saxena differiscono in modo sensibile, e con esse il coefficiente di scambio. Wilke e' la scelta fisicamente corretta; la media molare e' riportata solo perche' molti datasheet di fornitori la usano."

        hdr sb "6. Circolazione naturale"
        let cc = r.Circulation
        kv sb "Dislivello asse drum - asse WHB" (sprintf "%s m" (f2 c.Loop.DzDrumWhb))
        kv sb "H discesa / H fascio / H salita" (sprintf "%s / %s / %s m" (f2 cc.HDowncomer) (f2 cc.HShell) (f2 cc.HRiser))
        kv sb "Downcomer" (Circulation.branchDescription c.Loop.Downcomers)
        kv sb "Riser" (Circulation.branchDescription c.Loop.Risers)
        kv sb "RAPPORTO DI CIRCOLAZIONE (CR)" (f1 cc.CirculationRatio)
        kv sb "Portata circolante" (sprintf "%s kg/s  (%s t/h)" (f1 cc.CircFlow) (f0 (cc.CircFlow * 3.6)))
        kv sb "Titolo uscita fascio / riser" (sprintf "%s / %s" (f4 cc.XOutBundle) (f4 cc.XOutRiser))
        kv sb "Frazione di vuoto fascio / riser" (sprintf "%s / %s" (f3 cc.AlphaOutBundle) (f3 cc.AlphaOutRiser))
        kv sb "Battente motore netto" (sprintf "%s mbar" (f1 (cc.DrivingHead / 100.0)))
        kv sb "  - perdite downcomer" (sprintf "%s mbar" (f1 (cc.DpDowncomer / 100.0)))
        kv sb "  - perdite riser" (sprintf "%s mbar" (f1 (cc.DpRiser / 100.0)))
        kv sb "  - attraversamento fascio (medio)" (sprintf "%s mbar" (f1 (cc.DpBundle / 100.0)))
        kv sb "  - interne drum" (sprintf "%s mbar" (f1 (cc.DpNozzles / 100.0)))
        kv sb "Velocita' downcomer / riser" (sprintf "%s / %s m/s" (f2 cc.VelDowncomer) (f2 cc.VelRiserMix))
        kv sb "CR EFFICACE VISTO DAI TUBI" (f1 cc.EffectiveCR)
        kv sb "Corona anulare aperta" (sprintf "%s m2/m (diaframma OD %s mm)" (f4 cc.OpenAnnulus) (f0 (c.Tube.BaffleOd * 1000.0)))
        kv sb "Frazione by-pass (neg. = discesa interna)" (sprintf "%s %%" (f1 (100.0 * cc.BypassFraction)))
        kv sb "Alpha nella corona / titolo di rientro" (sprintf "%s / %s" (f3 cc.BypassAlpha) (f4 cc.XCarryUnder))
        kv sb "Velocita' di risalita bolle (drift)" (sprintf "%s m/s" (f3 (Circulation.driftVelocity r.Sat)))
        kv sb "Modello vuoto / attrito bifase"
            (sprintf "%s | %s" (TwoPhase.voidModelName c.Loop.VoidModel) (TwoPhase.frictionModelName c.Loop.FrictionModel))
        kv sb "Battente disponibile positivo" (if cc.Converged then "SI" else "NO - circuito non sostenibile")

        sb.AppendLine() |> ignore
        para sb "  " (sprintf "IN PAROLE SEMPLICI. L'acqua gira in un anello chiuso e SENZA POMPA: scende dal corpo cilindrico nei tubi di discesa (downcomer), attraversa il fascio dove in parte diventa vapore, e risale nei tubi di salita (riser) perche' la miscela acqua+vapore e' piu' leggera dell'acqua sola. E' lo stesso principio del camino. La 'benzina' del sistema e' il BATTENTE MOTORE (%s mbar): la differenza di peso fra la colonna che scende e quelle che salgono. Tutta questa spinta viene consumata dagli attriti del giro, elencati sotto. Il RAPPORTO DI CIRCOLAZIONE dice quante volte l'acqua fa il giro per ogni chilo di vapore prodotto: %s significa che di 100 kg che escono dal mantello, %s sono vapore e il resto torna indietro liquido. Serve che sia almeno 10, perche' l'acqua in eccesso e' quella che lava i tubi e impedisce che si scoprano. Le altezze H sono le tre quote in gioco, misurate dal livello dell'acqua nel corpo cilindrico." (f1 (cc.DrivingHead / 100.0)) (f1 cc.CirculationRatio) (f1 (100.0 / cc.CirculationRatio)))
        sb.AppendLine() |> ignore

        hdr sb "6b. Verifica dei riser: regime di moto bifase (Taitel-Dukler)"
        sb.AppendLine("  riser                    ID[mm]   jl[m/s]  jv[m/s]  Vmix[m/s]  alpha   rho*v2   regime") |> ignore
        sb.AppendLine(line) |> ignore
        for rc in r.RiserChecks do
            sb.AppendLine(
                sprintf "  %-24s %7s %9s %8s %10s %6s %8s   %s"
                    rc.Label (f1 (rc.Id * 1000.0)) (f2 rc.VelSuperficialLiq) (f2 rc.VelSuperficialVap)
                    (f2 rc.VelMix) (f3 rc.Alpha) (f0 rc.RhoV2) (Mechanics.regimeName rc.Regime)) |> ignore
        sb.AppendLine(line) |> ignore
        legend sb
            [ "ID [mm]", "Diametro interno del singolo riser."
              "jl [m/s]", "Velocita' SUPERFICIALE del liquido: portata volumetrica di liquido divisa per l'intera sezione del tubo, come se il liquido la occupasse da solo. Non e' la velocita' reale del liquido."
              "jv [m/s]", "Velocita' superficiale del vapore, definita allo stesso modo. La coppia (jl, jv) e' quella che individua il punto sulla mappa dei regimi di moto."
              "Vmix [m/s]", "Velocita' della miscela calcolata con la densita' omogenea: e' la velocita' che si usa per rho*v2 e per le verifiche di erosione."
              "alpha", "Frazione di vuoto nel riser (area di vapore / area totale)."
              "rho*v2", "Quantita' di moto specifica del flusso [kg/(m s2)]: e' il parametro con cui si giudicano impingement, erosione e forzanti vibrazionali sui bocchelli."
              "regime", "Regime di moto bifase secondo la mappa di Taitel-Dukler per moto verticale ascendente. Da evitare il regime a TAPPI (slug), che pulsa a 0.5-5 Hz ed eccita i supporti." ]
        sb.AppendLine(sprintf "  Diametro minimo perche' possa formarsi la bolla di Taylor (moto a tappi): %s mm."
                          (f1 (Mechanics.dMinForSlug r.Sat * 1000.0))) |> ignore
        for rc in r.RiserChecks do
            sb.AppendLine(sprintf "  %s: %s" rc.Label rc.Note) |> ignore
        let vDc = r.Circulation.VelDowncomer
        let dDc = (c.Loop.Downcomers |> List.map (fun b -> b.Id) |> List.max)
        sb.AppendLine(sprintf "  Downcomer: sommergenza minima dello stacco dal drum %s m (criterio di Froude anti-vortice)."
                          (f2 (Mechanics.minSubmergence dDc vDc))) |> ignore

        match r.DrumResult with
        | None -> ()
        | Some d ->
            let dm = c.Loop.Drum
            hdr sb "6c. Corpo cilindrico: perdite di carico e separazione"
            kv sb "Geometria" (sprintf "ID %s mm x %s mm T-T, livello normale %s mm dal fondo"
                                   (f0 (dm.ShellId * 1000.0)) (f0 (dm.Length * 1000.0)) (f0 (dm.NormalLevel * 1000.0)))
            kv sb "Interne sul percorso di circolazione"
                (sprintf "%d calm box, %d riser(s)/box, canale %s m2, apertura superiore %s m2, caduta acqua %s m, ingresso downcomer %s m2, K vortex breaker %s, scarico %s"
                     dm.ConveyorCount dm.CalmBoxRisersPerBox (f3 dm.ConvDuctArea) (f3 dm.ConvOutletArea)
                     (f2 dm.CalmBoxWaterFallHeight)
                     (if dm.DowncomerEntryArea > 0.0 then f3 dm.DowncomerEntryArea else "auto")
                     (f2 dm.DowncomerVortexBreakerK)
                     (if dm.ConvOutletAboveLevel then "SOPRA il livello (spazio vapore)" else "SOMMERSO"))
            para sb "  " "Metodo calm-box: la perdita di circolazione include uscita dei riser nella camera, transito nella scatola, apertura superiore con eventuale caduta dell'acqua e imbocco dei downcomer con vortex breaker. I cicloni non sono considerati in questa versione; camini con top-hat direttamente sui riser sono una futura alternativa di modellazione."
            sb.AppendLine() |> ignore
            sb.AppendLine("  A) PERCORSO DI CIRCOLAZIONE - e' l'unica perdita che entra nel bilancio del battente") |> ignore
            sb.AppendLine("  voce                                                     K      A[m2]   v[m/s]  rho[kg/m3]  dP[mbar]") |> ignore
            sb.AppendLine(line) |> ignore
            for it in d.CircItems do
                sb.AppendLine(
                    sprintf "  %-52s %7s %9s %8s %11s %9s"
                        (if it.Label.Length > 52 then it.Label.Substring(0, 52) else it.Label)
                        (f3 it.K) (f4 it.Area) (f2 it.Velocity) (f1 it.Rho) (f2 (it.Dp / 100.0))) |> ignore
            sb.AppendLine(line) |> ignore
            sb.AppendLine(sprintf "  %-52s %7s %9s %8s %11s %9s" "TOTALE sul percorso di circolazione" "" "" "" ""
                              (f2 (d.DpCirculation / 100.0))) |> ignore
            (match dm.VendorDpCirculation with
             | Some v -> kv sb "  (VALORE DEL COSTRUTTORE, sovrascrive il calcolo)" (sprintf "%s mbar" (f1 (v / 100.0)))
             | None -> ())
            sb.AppendLine() |> ignore
            for it in d.CircItems do
                if it.Note <> "" then
                    sb.AppendLine(sprintf "    %-52s %s" it.Label it.Note) |> ignore
            sb.AppendLine() |> ignore
            sb.AppendLine("  A2) SENSIBILITA' AL COEFFICIENTE DEL CONVOGLIATORE (e' il parametro dominante)") |> ignore
            sb.AppendLine("  K extra   K totale netto   dP interne[mbar]   quota del battente motore[%]") |> ignore
            sb.AppendLine(line) |> ignore
            let head0 =
                0.5 * TwoPhase.homogeneousDensity r.Circulation.XOutRiser r.Sat
                * (r.Circulation.CircFlow
                   / (TwoPhase.homogeneousDensity r.Circulation.XOutRiser r.Sat
                      * Circulation.branchArea c.Loop.Risers)) ** 2.0
            for kx in [ 0.0; 0.5; 1.0; 1.5; 2.0; 3.0; 4.0 ] do
                let dd =
                    Drum.solve { dm with ConvExtraK = kx; VendorDpCirculation = None } r.Sat
                        r.Circulation.CircFlow r.Circulation.XOutRiser r.Circulation.SteamFlow
                        (Circulation.branchArea c.Loop.Risers) (Circulation.branchArea c.Loop.Downcomers)
                sb.AppendLine(
                    sprintf "  %7s %16s %18s %30s%s"
                        (f2 kx) (f3 (dd.DpCirculationNet / head0)) (f1 (dd.DpCirculation / 100.0))
                        (f1 (100.0 * dd.DpCirculation / max 1.0 r.Circulation.DrivingHead))
                        (if abs (kx - dm.ConvExtraK) < 1e-6 then "   <== assunto" else "")) |> ignore
            sb.AppendLine(line) |> ignore
            para sb "  " (sprintf "Il valore assunto (%s) e' una stima ingegneristica per un canale in lamiera con transizione da tondo a rettangolare, telai interni e curva non ideale. La riga K = 0 rappresenta il canale idealmente liscio: si vede che in quel caso il convogliatore costa MENO di uno sbocco nudo, perche' rilascia la miscela piu' lentamente. E' il comportamento per cui i convogliatori esistono. Il valore vero sta fra questi estremi e lo conosce solo il costruttore." (f2 dm.ConvExtraK))
            sb.AppendLine() |> ignore
            sb.AppendLine("  B) PERCORSO VAPORE - NON entra nel bilancio di circolazione") |> ignore
            sb.AppendLine("  voce                                                     K      A[m2]   v[m/s]  rho[kg/m3]  dP[mbar]") |> ignore
            sb.AppendLine(line) |> ignore
            for it in d.SteamItems do
                sb.AppendLine(
                    sprintf "  %-52s %7s %9s %8s %11s %9s"
                        (if it.Label.Length > 52 then it.Label.Substring(0, 52) else it.Label)
                        (f3 it.K) (f4 it.Area) (f2 it.Velocity) (f1 it.Rho) (f2 (it.Dp / 100.0))) |> ignore
            sb.AppendLine(line) |> ignore
            sb.AppendLine(sprintf "  %-52s %7s %9s %8s %11s %9s" "TOTALE dal pelo libero all'uscita vapore" "" "" "" ""
                              (f2 (d.DpSteam / 100.0))) |> ignore
            sb.AppendLine() |> ignore
            sb.AppendLine("  C) VERIFICHE DI SEPARAZIONE") |> ignore
            kv sb "Area del pelo libero" (sprintf "%s m2" (f2 d.SurfaceArea))
            kv sb "Velocita' superficiale del vapore al pelo libero"
                (sprintf "%s m/s su limite %s m/s (Souders-Brown K = 0.045)  ->  utilizzo %s %%"
                     (f4 d.VSurface) (f4 d.VSurfaceMax) (f0 (100.0 * d.SurfaceUtil)))
            kv sb "Velocita' frontale sul demister"
                (sprintf "%s m/s su limite %s m/s (K = 0.10)" (f3 d.VDemister) (f3 d.VDemisterMax))
            kv sb "Altezza dello spazio vapore" (sprintf "%s mm" (f0 ((dm.ShellId - dm.NormalLevel) * 1000.0)))
            kv sb "Sommergenza degli stacchi dei downcomer" (sprintf "%s mm" (f0 (dm.NormalLevel * 1000.0)))
            legend sb
                [ "K", "Coefficiente di perdita localizzata. In questa tabella tutti i K del percorso di circolazione sono RIPORTATI ALLA VELOCITA' NEL BOCCHELLO DEL RISER, cosi' si sommano direttamente e si confrontano con i valori di letteratura."
                  "A [m2]", "Sezione di passaggio a cui si riferisce la velocita' indicata nella riga."
                  "v [m/s]", "Velocita' effettiva in quella sezione (miscela omogenea sul percorso di circolazione, vapore saturo secco sul percorso vapore)."
                  "rho", "Densita' usata: omogenea della miscela sul percorso di circolazione, del vapore saturo su quello vapore."
                  "dP [mbar]", "Perdita di quella singola voce."
                  "DEDUZIONE", "Riga negativa: toglie lo sbocco K = 1.0 gia' conteggiato nella linea del riser (Piping.totalK). Senza questa deduzione la perdita sarebbe contata due volte." ]
            sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
            sb.AppendLine("  " + String('-', 92)) |> ignore
            para sb "  " "TRE COSE DIVERSE CHE SI CHIAMANO TUTTE 'PERDITA NEL CORPO CILINDRICO'. La prima non e' una perdita: e' il LIVELLO dell'acqua, che stabilisce dove comincia la colonna di discesa ed e' gia' contato nel battente. La seconda e' la perdita che la MISCELA subisce dentro il corpo cilindrico, dal bocchello del riser fino a quando l'acqua separata rientra nella massa: e' l'unica che toglie battente alla circolazione. La terza e' la perdita che il VAPORE subisce dal pelo libero fino al bocchello di uscita, attraverso demister e camini: non c'entra nulla con la circolazione, si scarica sulla pressione consegnata in rete. Confonderle e' l'errore piu' comune in questo calcolo."
            para sb "  " (sprintf "PERCHE' IL NUMERO PESA TANTO. La miscela arriva ai riser a %s m/s con densita' omogenea %s kg/m3: una sola altezza cinetica vale gia' %s mbar. Su un battente motore di poche centinaia di millibar, ogni unita' di K nelle interne si mangia una fetta importante della circolazione. E' per questo che il valore va calcolato e non assunto." (f2 (r.Circulation.VelRiserMix)) (f0 (TwoPhase.homogeneousDensity r.Circulation.XOutRiser r.Sat)) (f1 (0.5 * TwoPhase.homogeneousDensity r.Circulation.XOutRiser r.Sat * r.Circulation.VelRiserMix ** 2.0 / 100.0)))
            para sb "  " "COME SI CALCOLA. Si scompone il percorso in singolarita' elementari, ognuna con il suo coefficiente K preso da Idelchik: allargamento o restringimento brusco fra bocchello e canale (Borda-Carnot), attrito e curvatura nel canale del convogliatore, perdita dell'energia cinetica allo scarico. Per il bifase si usa il modello OMOGENEO, dp = K G^2 / (2 rho_H): in una singolarita' brusca vapore e liquido non hanno tempo di scorrere uno rispetto all'altro, quindi si comportano come un fluido solo. In alternativa si puo' usare il moltiplicatore di Chisholm per singolarita', che da' valori piu' alti quando il titolo e' basso e lo scorrimento conta."
            para sb "  " "CONVOGLIATORI O CICLONI, NON E' LO STESSO. In un corpo cilindrico a CICLONI tutta la miscela attraversa il ciclone, e la perdita del separatore - che e' grande - sta per intero sul percorso di circolazione. In un corpo cilindrico a CONVOGLIATORI, come questo, la miscela passa solo nel canale del convogliatore, che e' progettato per NON strozzare; il demister vede solo il vapore. E' il motivo per cui questa costruzione costa meno battente."
            para sb "  " (sprintf "LA VERIFICA DI SEPARAZIONE. Il vapore lascia il pelo libero a %s m/s contro un limite di Souders-Brown di %s m/s: si e' al %s%% del limite. E' il margine che permette di separare per sola gravita' piu' un demister, senza ricorrere ai cicloni. Se il carico salisse oltre il limite, le goccioline non riuscirebbero piu' a ricadere e finirebbero nel vapore (trascinamento)." (f4 d.VSurface) (f4 d.VSurfaceMax) (f0 (100.0 * d.SurfaceUtil)))
            para sb "  " "IL DATO CHE VALE PIU' DI TUTTI. Questo calcolo e' una ricostruzione dalla geometria di disegno. Il costruttore del corpo cilindrico ha la CURVA SPERIMENTALE dp-portata delle sue interne: se la si ottiene, la si inserisce come dato e il calcolo di circolazione smette di avere assunzioni aperte. E' l'unica richiesta veramente dirimente rimasta."
            sb.AppendLine("  " + String('-', 92)) |> ignore

        hdr sb "6d. Vibrazioni indotte dal flusso (FIV)"
        kv sb "Campate libere (da disegno)"
            (sprintf "%d campate: %s mm" (List.length c.BaffleSpans)
                 (c.BaffleSpans |> List.map (fun x -> f0 (x * 1000.0)) |> String.concat " / "))
        kv sb "CAMPATA GOVERNANTE"
            (sprintf "%s mm, la PRIMA, cioe' fra la piastra tubiera lato gas caldo e il primo diaframma"
                 (f0 ((c.BaffleSpans |> List.max) * 1000.0)))
        kv sb "Spessore diaframmi" (sprintf "%s mm" (f0 (c.BaffleThickness * 1000.0)))
        kv sb "VINCOLO AI DIAFRAMMI"
            "NODO SEMPLICE - il foro impedisce lo spostamento laterale ma NON la rotazione (lambda2 = 9.87)"
        kv sb "VINCOLO ALLE PIASTRE TUBIERE" (Vibration.jointName c.TubesheetJoint)
        para sb "  " "Trattare i diaframmi come incastri sarebbe un errore grave e NON conservativo: porterebbe lambda2 da 9.87 a 22.37, cioe' sovrastimerebbe la frequenza propria del 127 % e con essa la velocita' critica. Il foro del diaframma trattiene il tubo lateralmente ma lo lascia ruotare liberamente."
        kv sb "Verifica somma campate"
            (sprintf "%s mm di campate + %d diaframmi x %s mm = %s mm contro %s m di lunghezza tubi"
                 (f0 ((List.sum c.BaffleSpans) * 1000.0)) (List.length c.BaffleSpans - 1)
                 (f0 (c.BaffleThickness * 1000.0))
                 (f0 ((List.sum c.BaffleSpans + float (List.length c.BaffleSpans - 1) * c.BaffleThickness) * 1000.0))
                 (f3 c.Tube.Length))
        kv sb "RETICOLO (TEMA RCB-2.4)" (Vibration.layoutName c.TubeLayout)
        kv sb "Costante di Connors per questo reticolo in bifase"
            (sprintf "K = %s  -  fonte: guide di progetto specifiche per flusso bifase (J. Zhejiang Univ. SCIENCE A). Confronto: K = 3.0 e' l'inviluppo GENERALE di Pettigrew-Taylor su tutte le configurazioni, K = 4.0 il valore del triangolare NORMALE"
                 (f1 (r.Vibration |> List.head).KConnors))
        kv sb "Decremento logaritmico" (sprintf "delta = %s (estremo basso del campo 0.03-0.10 misurato in crossflow bifase)" (f3 c.VibrationDamping))
        sb.AppendLine() |> ignore
        sb.AppendLine("  banda   y[m]  campata  f propria  m lin  C massa   V varco  V critica  V/Vcrit   f vortici  f/fn   f buffet  f/fn  esito") |> ignore
        sb.AppendLine("                  [m]       [Hz]  [kg/m]           [m/s]      [m/s]                 [Hz]              [Hz]") |> ignore
        sb.AppendLine(line) |> ignore
        for vb in r.Vibration do
            sb.AppendLine(
                sprintf "  %5d %6s %8s %10s %6s %8s %9s %10s %8s %11s %6s %10s %5s  %s"
                    vb.Band (f2 vb.Y) (f3 vb.Span) (f1 vb.FreqNat) (f2 vb.MassLin) (f2 vb.Cm)
                    (f2 vb.VGap) (f2 vb.VCrit) (f3 vb.FeiRatio)
                    (f1 vb.FreqVortex) (f3 vb.VortexRatio) (f1 vb.FreqBuffet) (f3 vb.BuffetRatio)
                    (if vb.FeiRatio >= 1.0 then "CRITICO" elif vb.FeiRatio >= 0.8 then "ATTENZIONE" else "ok")) |> ignore
        sb.AppendLine(line) |> ignore
        let vWorst = r.Vibration |> List.maxBy (fun x -> x.FeiRatio)
        kv sb "COMBINAZIONE GOVERNANTE"
            (sprintf "banda %d (y = %+.2f m) sulla campata da %s mm: V/Vcrit = %s"
                 vWorst.Band vWorst.Y (f0 (vWorst.Span * 1000.0)) (f3 vWorst.FeiRatio))
        para sb "  " "La combinazione peggiore non e' semplicemente la banda piu' veloce ne' la campata piu' lunga: e' il loro incrocio. Qui il disegno le mette nello stesso punto, perche' la campata piu' lunga (1290 mm) e' all'estremita' calda, dove la generazione di vapore e quindi la velocita' di attraversamento sono massime."
        kv sb "CAMPATA MASSIMA per V/Vcrit = 0.8" (sprintf "%s m" (f2 (Vibration.maxSpan 0.8 vWorst)))
        kv sb "CAMPATA MASSIMA per V/Vcrit = 1.0" (sprintf "%s m" (f2 (Vibration.maxSpan 1.0 vWorst)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  SENSIBILITA' ALLA CAMPATA (il rapporto cresce con il QUADRATO della campata)") |> ignore
        sb.AppendLine("  campata[m]   f propria[Hz]   V critica[m/s]   V/Vcrit   esito") |> ignore
        sb.AppendLine(line) |> ignore
        for sp in [ 0.6; 0.8; 1.0; 1.2; 1.5; 1.8; 2.2 ] do
            let bandCells = r.Cells |> List.filter (fun x -> x.J = vWorst.Band)
            let w = bandCells |> List.maxBy (fun x -> x.VelCross)
            let vb =
                Vibration.check vWorst.Band vWorst.Y sp 22.37 c.TubeLayout c.VibrationDamping
                    c.Tube.Do c.Tube.Di c.Tube.Pitch (c.Material.E (kToC w.TMetalWallAvg)) 7850.0
                    w.VelCross vWorst.Rho 10.0
            sb.AppendLine(
                sprintf "  %10s %15s %16s %9s   %s"
                    (f2 sp) (f1 vb.FreqNat) (f2 vb.VCrit) (f3 vb.FeiRatio)
                    (if vb.FeiRatio >= 1.0 then "CRITICO" elif vb.FeiRatio >= 0.8 then "ATTENZIONE" else "ok")) |> ignore
        sb.AppendLine(line) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  MATRICE DI SENSIBILITA' K x delta - E' QUI CHE SI DECIDE") |> ignore
        sb.AppendLine("  Il rapporto V/Vcrit nella banda governante, e fra parentesi la CAMPATA MASSIMA") |> ignore
        sb.AppendLine("  ammessa perche' V/Vcrit resti sotto 1.0, per ogni coppia di costanti.") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("     K   fonte / reticolo                                delta=0.03      delta=0.06      delta=0.10") |> ignore
        sb.AppendLine(line) |> ignore
        for (kk, src) in
            [ 1.1, "60° triang. RUOTATO, bifase (mdp <= 0.54)  <== QUESTO CASO"
              1.5, "60° triang. ruotato, bifase (mdp > 0.54)"
              3.0, "inviluppo generale Pettigrew-Taylor"
              4.0, "30° triangolare NORMALE, bifase"
              4.8, "media dei dati sperimentali monofase" ] do
            let cell (dd: float) =
                let rr = Vibration.ratioWith kk dd vWorst
                let ls = Vibration.maxSpanWith 1.0 kk dd vWorst
                sprintf "%5s (%4s m)" (f2 rr) (f2 ls)
            sb.AppendLine(
                sprintf "  %4s   %-46s %-15s %-15s %-15s"
                    (f1 kk) src (cell 0.03) (cell 0.06) (cell 0.10)) |> ignore
        sb.AppendLine(line) |> ignore
        para sb "  " (sprintf "Il parametro di massa-smorzamento di questo apparecchio vale %.3f, quindi sotto la soglia di 0.54 che separa i due valori del reticolo ruotato." vWorst.MassDamping)
        sb.AppendLine() |> ignore
        sb.AppendLine("  LE ALTRE DUE INCERTEZZE, CHE PESANO QUANTO O PIU' DELLA COSTANTE DI CONNORS") |> ignore
        sb.AppendLine(line) |> ignore
        let vMean =
            let bandCells = r.Cells |> List.filter (fun x -> x.J = vWorst.Band)
            bandCells |> List.averageBy (fun x -> x.VelCross)
        let lamUsed =
            match c.TubesheetJoint with
            | Vibration.FullPenetrationWeld -> 15.42
            | Vibration.CreviceFreeWeld -> 9.87
        sb.AppendLine("  1) VINCOLO ALLE ESTREMITA' DELLA CAMPATA (la campata governante tocca la piastra)") |> ignore
        sb.AppendLine(sprintf "     appoggio-appoggio, saldatura crevice-free (lambda2 =  9.87)   V/Vcrit = %s"
                          (f2 (vWorst.FeiRatio * lamUsed / 9.87))) |> ignore
        sb.AppendLine(sprintf "     incastro-appoggio, piena penetrazione     (lambda2 = 15.42)   V/Vcrit = %s"
                          (f2 (vWorst.FeiRatio * lamUsed / 15.42))) |> ignore
        sb.AppendLine(sprintf "     [se i diaframmi fossero incastri - IPOTESI ERRATA (lambda2 = 22.37)  V/Vcrit = %s]"
                          (f2 (vWorst.FeiRatio * lamUsed / 22.37))) |> ignore
        sb.AppendLine(sprintf "     valore usato nel calcolo: %s" (Vibration.jointName c.TubesheetJoint)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  2) DISTRIBUZIONE ASSIALE DEL CROSSFLOW - e' l'incertezza PIU' GRANDE") |> ignore
        sb.AppendLine(sprintf "     velocita' usata (rapporto di circolazione LOCALE uniforme)   %s m/s   V/Vcrit = %s"
                          (f2 vWorst.VGap) (f2 vWorst.FeiRatio)) |> ignore
        sb.AppendLine(sprintf "     velocita' MEDIA sulla banda (crossflow uniforme lungo z)     %s m/s   V/Vcrit = %s"
                          (f2 vMean) (f2 (vWorst.FeiRatio * vMean / vWorst.VGap))) |> ignore
        sb.AppendLine(sprintf "     rapporto fra le due                                          %s" (f2 (vWorst.VGap / vMean))) |> ignore
        sb.AppendLine(line) |> ignore
        para sb "  " (sprintf "IL MODELLO ASSUME CHE LA PORTATA CIRCOLANTE LOCALE SEGUA LA PRODUZIONE LOCALE DI VAPORE - rapporto di circolazione locale uniforme. Poiche' la generazione di vapore e' fortemente concentrata all'imbocco del gas, dove il flusso termico e' quattro-cinque volte la media, anche la velocita' di attraversamento risulta concentrata li': %s m/s contro %s m/s di media sulla banda, un fattore %s. E' proprio dove cade anche la campata piu' lunga." (f2 vWorst.VGap) (f2 vMean) (f2 (vWorst.VGap / vMean)))
        para sb "  " "L'ASSUNZIONE E' DIFENDIBILE ma non e' l'unica possibile, e va dichiarata perche' pesa piu' della costante di Connors. E' giustificata dal fatto che i diaframmi, con gioco di 0.4 mm sui tubi e 5 mm sul mantello, bloccano quasi del tutto il flusso ASSIALE nel mantello: ogni compartimento fra due diaframmi e' idraulicamente quasi isolato, quindi l'acqua che evapora in un compartimento deve essere entrata in quel compartimento. Se invece esistesse una redistribuzione assiale apprezzabile, la velocita' di picco si abbasserebbe verso la media e il rapporto V/Vcrit scenderebbe nella stessa proporzione."
        para sb "  " "LA CONSEGUENZA PER LA DECISIONE. Fra costante di Connors (fattore 4), vincolo alle estremita' (fattore 2.3 sulla frequenza) e distribuzione del crossflow (fattore 4-5 sulla velocita'), il rapporto V/Vcrit e' incerto di oltre un ordine di grandezza. Questo NON significa che il risultato sia inutile: significa che indica DOVE guardare - banda alta, prima campata, estremita' calda - e che la risposta quantitativa deve venire dal calcolo di vibrazione del costruttore e, se esiste, dalla storia di esercizio. Un intervento sui diaframmi basato solo su questi numeri sarebbe prematuro."
        legend sb
            [ "f propria [Hz]", "Frequenza del primo modo di flessione del tubo fra due diaframmi, f = (lambda^2/2pi) sqrt(EI/(m L^4)), con lambda^2 = 22.37 per estremi incastrati. Il gioco di 0.40 mm sul diametro giustifica l'incastro."
              "m lin [kg/m]", "Massa vibrante per metro: metallo, piu' il gas dentro il tubo, piu' la MASSA AGGIUNTA idrodinamica, cioe' il fluido esterno che il tubo deve muovere quando oscilla."
              "C massa", "Coefficiente di massa aggiunta per un tubo dentro un fascio (TEMA V-6): in un reticolo fitto vale molto piu' che per un cilindro isolato."
              "V varco [m/s]", "Velocita' della miscela nel varco fra due tubi: e' quella che eccita, non la velocita' media sul mantello."
              "V critica", "Velocita' oltre la quale scatta l'INSTABILITA' FLUIDO-ELASTICA (Connors): V_cr = K f_n D sqrt(m delta/(rho D^2))."
              "V/Vcrit", "Criterio principale. Limite di progetto 0.8; sopra 1.0 l'ampiezza diverge."
              "f vortici", "Frequenza di distacco dei vortici, f = St V/D. Rischio di aggancio se f/fn cade fra 0.5 e 2."
              "f buffet", "Frequenza caratteristica dell'eccitazione turbolenta a banda larga (Owen). Non produce collasso ma fatica e usura ai supporti." ]
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " "PERCHE' QUESTA VERIFICA E' A SE'. Un tubo puo' essere perfettamente verificato a temperatura, pressione e dilatazione e rompersi lo stesso in poche migliaia di ore, perche' VIBRA. Nei fasci attraversati da crossflow e' la causa di rottura piu' comune, e non ha nulla a che vedere con la termica: dipende solo da velocita', densita', geometria e campata."
        para sb "  " "L'INSTABILITA' FLUIDO-ELASTICA E' IL MECCANISMO PERICOLOSO, ed e' diverso da una risonanza. Non c'e' una frequenza da evitare: c'e' una VELOCITA' da non superare. Sotto quella velocita' il fluido smorza le oscillazioni del tubo; sopra, il tubo estrae energia dal flusso a ogni ciclo e l'ampiezza cresce senza limite, fino all'urto contro i tubi vicini o alla rottura per fatica proprio in corrispondenza del diaframma. Non c'e' un ginocchio graduale: e' un interruttore."
        para sb "  " (sprintf "IL RISULTATO DIPENDE DALLA CAMPATA CON IL QUADRATO. La frequenza propria va come 1/L^2, e la velocita' critica e' proporzionale alla frequenza propria: quindi il rapporto V/Vcrit cresce come L^2. Dimezzare la campata lo divide per quattro. Con la campata assunta di %s m il rapporto vale %s; la campata massima per restare sotto 0.8 e' %s m. Poiche' il passo dei diaframmi e' variabile lungo l'apparecchio, e' la CAMPATA PIU' LUNGA a decidere, e va confrontata con questo valore." (f2 c.UnsupportedSpan) (f3 vWorst.FeiRatio) (f2 (Vibration.maxSpan 0.8 vWorst)))
        para sb "  " "LE COSTANTI CONTANO QUANTO LA GEOMETRIA. K di Connors vale 3.0 come inviluppo inferiore dei dati sperimentali, 3.3 nella formulazione classica, e i dati arrivano a 10 per certi reticoli. Il decremento logaritmico in flusso bifase e' misurato fra 0.03 e 0.10. Qui si e' usato K = 3.0 e delta = 0.03, cioe' la combinazione piu' conservativa: con K = 4.5 e delta = 0.06 la velocita' critica raddoppia abbondantemente. Se il rapporto risulta marginale, la prima cosa da fare non e' cambiare il progetto ma procurarsi i valori giusti per questo reticolo."
        para sb "  " "GLI ALTRI TRE MECCANISMI. Il distacco dei vortici da' risonanza solo se la sua frequenza si avvicina a quella propria; in flusso BIFASE e' comunque molto attenuato, perche' le bolle distruggono la coerenza della scia. Il buffeting turbolento e' eccitazione casuale a banda larga: non fa collassare nulla ma consuma i supporti e apre cricche di fatica nel lungo periodo. La risonanza acustica riguarda solo il gas comprimibile a mantello e qui non si applica, perche' a mantello c'e' acqua in ebollizione."

        hdr sb "6f. Maldistribuzione della portata di gas fra i tubi"
        para sb "  " "COS'E'. Il calcolo assume che gli 848 tubi ricevano tutti la stessa portata di gas. Nella realta' la camera d'ingresso non distribuisce in modo perfetto: i tubi affacciati al getto del bocchello ne ricevono di piu', quelli in ombra di meno. Questa sezione misura che cosa succede al tubo PIU' CARICATO."
        para sb "  " "COME E' MODELLATA. I tubi sono canali in PARALLELO che non si scambiano calore fra loro: un singolo tubo sbilanciato non altera ne' la circolazione ne' la produzione di vapore dell'apparecchio. Si marcia quindi UN SOLO tubo con la portata maggiorata, tenendo congelato tutto il lato mantello: temperatura di saturazione, coefficiente di ebollizione, sporcamento, flusso critico locale. Cambia solo la resistenza lato gas, che e' quella governata dalla portata. E' il modo corretto di isolare il comportamento del singolo canale."
        sb.AppendLine() |> ignore
        sb.AppendLine("  eccesso   w tubo    Re ing.   h gas picco   q'' picco   z picco   T met.int   T gas usc.   DNBR   potenza tubo") |> ignore
        sb.AppendLine("      [%]    [g/s]                 [W/m2K]     [kW/m2]       [m]        [°C]         [°C]           [kW]") |> ignore
        sb.AppendLine(line) |> ignore
        for m in r.Maldistribution do
            sb.AppendLine(
                sprintf "  %7s %8s %10s %13s %11s %9s %11s %12s %6s %13s"
                    (f0 (100.0 * m.Excess)) (f1 (m.FlowPerTube * 1000.0)) (f0 m.ReIn)
                    (f0 m.HGasPeak) (f1 (m.QFluxMax / 1000.0)) (f2 m.ZQMax)
                    (f1 (kToC m.TMetalInMax)) (f1 (kToC m.TGasOut)) (f2 m.DNBRMin)
                    (f1 (m.DutyTube / 1000.0))) |> ignore
        sb.AppendLine(line) |> ignore
        (let b = r.Maldistribution |> List.head
         let w10 = r.Maldistribution |> List.tryFind (fun m -> abs (m.Excess - 0.10) < 1e-6)
         match w10 with
         | Some m ->
            kv sb "SENSIBILITA' a +10 % di portata"
                (sprintf "flusso di picco %+.1f %%, T metallo %+.1f K, DNBR %+.3f, T gas uscita %+.1f K"
                     (100.0 * (m.QFluxMax / b.QFluxMax - 1.0)) (m.TMetalInMax - b.TMetalInMax)
                     (m.DNBRMin - b.DNBRMin) (m.TGasOut - b.TGasOut))
         | None -> ())
        legend sb
            [ "eccesso [%]", "Portata in piu' che il tubo considerato riceve rispetto al tubo medio. Zero e' il tubo medio, cioe' il caso di riferimento del resto del report."
              "w tubo [g/s]", "Portata di gas in quel singolo tubo. Il tubo medio porta la portata totale (al netto del by-pass) divisa per il numero di tubi."
              "Re ing.", "Numero di Reynolds all'ingresso del tubo, dentro la ferrula. E' quello che governa il coefficiente di scambio."
              "h gas picco", "Coefficiente di scambio lato gas nel punto di flusso massimo, comprensivo di irraggiamento e correzioni. Cresce come la portata alla 0.8."
              "q'' picco", "Flusso termico massimo lungo QUEL tubo, riferito alla superficie esterna, escluso il tratto sotto ferrula."
              "z picco", "Ascissa del punto di flusso massimo, misurata dalla piastra tubiera lato gas caldo."
              "T met.int", "Temperatura massima del metallo sulla faccia interna di quel tubo: e' il numero da confrontare con il limite metallurgico."
              "T gas usc.", "Temperatura del gas all'uscita di quel tubo. SALE con la portata, perche' il gas ha meno tempo per raffreddarsi."
              "DNBR", "Margine su crisi di ebollizione per quel tubo, con il flusso critico locale del lato mantello, che non cambia."
              "potenza tubo", "Calore scambiato da quel singolo tubo. Cresce con la portata, ma meno che proporzionalmente." ]
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " "PERCHE' IL TUBO PIU' CARICATO E' DOPPIAMENTE SFAVORITO. Chi riceve piu' gas scambia di piu', e a prima vista sembrerebbe un vantaggio. Non lo e', per due ragioni che si sommano. La prima: il coefficiente di scambio lato gas cresce come la portata elevata a 0.8, quindi il flusso termico locale cresce quasi in proporzione alla portata, e con esso la temperatura del metallo e il rischio di crisi di ebollizione. La seconda: quel tubo ha meno tempo di residenza, quindi il suo gas resta piu' caldo lungo tutto il percorso, e a parita' di ascissa la differenza di temperatura che spinge il calore e' maggiore. Il risultato e' che lo stesso tubo e' contemporaneamente quello con il DNBR peggiore, quello con il metallo piu' caldo, e quello che consegna il gas piu' caldo all'apparecchiatura a valle."
        para sb "  " "PERCHE' NON SI VEDE NEL BILANCIO GLOBALE. La maldistribuzione non cambia la potenza totale in modo apprezzabile, perche' quello che un tubo prende in piu' un altro lo prende in meno, e il bilancio si compensa quasi esattamente. E' un problema LOCALE: si manifesta come rottura di alcuni tubi, non come perdita di prestazione. E' per questo che non lo si trova guardando i dati di esercizio, ma solo guardando dove si rompono i tubi."
        para sb "  " "IL LATO MANTELLO NON CAMBIA, ED E' IL PUNTO CHIAVE DEL MODELLO. Gli 848 tubi sono canali in parallelo immersi nella stessa massa d'acqua: non si scambiano calore fra loro. Un tubo che porta il 10 % di gas in piu' non altera la circolazione naturale, ne' la produzione di vapore, ne' il flusso critico locale, perche' tutte queste grandezze dipendono dall'insieme. Sarebbe un errore rifare il calcolo dell'intero apparecchio con la portata maggiorata: si otterrebbe un apparecchio piu' potente, non un tubo sbilanciato. Qui si marcia un solo tubo tenendo congelato tutto il resto."
        para sb "  " "DA DOVE NASCE. La camera d'ingresso deve trasformare il getto di un bocchello nella distribuzione uniforme su un'intera piastra tubiera. Con bocchello assiale centrato e camera profonda il getto ha spazio per aprirsi e la disuniformita' resta entro il 5-10 %. Con ingresso laterale, camera corta, o un deflettore mal posizionato, i tubi affacciati al getto ricevono il 20-30 % in piu' degli altri. La forma della camera e' quindi un dato di progetto termico, non solo meccanico."
        para sb "  " "COME SI LEGGE QUESTA TABELLA. Non e' una previsione: e' una scala. Non si sa quanto vale la maldistribuzione reale finche' non si conosce la geometria della camera d'ingresso, quindi la tabella dice quanto costa ogni livello di disuniformita'. Serve a rispondere a due domande: quanto margine occorre lasciare in progetto, e - se in esercizio si rompessero tubi tutti nella stessa zona della piastra - se la maldistribuzione basti a spiegarlo."
        para sb "  " "LIMITE DEL MODELLO. Si tiene congelato il profilo di pressione del gas e il coefficiente di irraggiamento presi dalla soluzione di base. Sono approssimazioni buone perche' entrambi variano poco con la portata del singolo tubo. Non si rappresenta invece la retroazione opposta, cioe' il fatto che un tubo che scambia di piu' diventa piu' resistente idraulicamente e quindi tende a riequilibrarsi: e' un effetto stabilizzante, quindi trascurarlo e' conservativo."

        hdr sb "6e. Transitori e protezione"
        let tr = r.Transient
        kv sb "Costante di tempo termica del metallo" (sprintf "%s s" (f1 tr.TauMetal))
        kv sb "Volume libero a mantello" (sprintf "%s m3 (frazione di vuoto media %s)" (f1 tr.ShellFreeVolume) (f3 tr.AlphaMean))
        kv sb "Inventario d'acqua liquida a MANTELLO" (sprintf "%s kg" (f0 tr.WaterInventory))
        kv sb "Inventario d'acqua nel CORPO CILINDRICO (liv. normale)" (sprintf "%s kg" (f0 tr.DrumInventory))
        kv sb "INVENTARIO TOTALE DISPONIBILE" (sprintf "%s kg" (f0 (tr.WaterInventory + tr.DrumInventory)))
        sb.AppendLine() |> ignore
        kv sb "SCENARIO 1 - perdita acqua alimento, circolazione attiva"
            (sprintf "%s s  (%s min) - i downcomer scendono per gravita': disponibile tutto l'inventario"
                 (f0 tr.TimeToDryout) (f1 (tr.TimeToDryout / 60.0)))
        kv sb "SCENARIO 2 - blocco della circolazione (CASO SEVERO)"
            (sprintf "%s s  (%s min) - downcomer ostruiti: resta il solo inventario del mantello"
                 (f0 tr.TimeToDryoutIsolated) (f1 (tr.TimeToDryoutIsolated / 60.0)))
        kv sb "Portata di reintegro per compensare la potenza" (sprintf "%s kg/s (%s t/h)" (f1 tr.MakeupRate) (f0 (tr.MakeupRate * 3.6)))
        kv sb "T metallo di equilibrio dopo il dry-out" (sprintf "%s °C  su limite %s °C" (f0 (kToC tr.TMetalDryout)) (f0 c.Material.TmaxDesign))
        kv sb "Tempo per avvicinarla (3 costanti di tempo)" (sprintf "%s s" (f0 tr.TimeToOverheat))
        legend sb
            [ "costante di tempo", "Inerzia termica del metallo verso l'acqua, tau = m c /(h A). Dice in quanto tempo la temperatura del tubo insegue una variazione del gas."
              "inventario", "Massa di acqua LIQUIDA presente a mantello nelle condizioni di esercizio, cioe' al netto del vapore gia' formato."
              "tempo di evaporazione a secco", "Tempo per evaporare tutto l'inventario se la circolazione si ferma di colpo e la potenza resta quella nominale. E' il caso peggiore."
              "T di equilibrio dopo dry-out", "Temperatura verso cui tende il metallo quando resta il solo vapore a raffreddare." ]
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " (sprintf "IL METALLO NON HA INERZIA. La costante di tempo termica e' di %s secondi: il tubo segue la temperatura del gas praticamente in tempo reale. Non c'e' nessun cuscinetto termico che protegga da un transitorio del processo, e non ha senso sperare che 'una punta breve non faccia in tempo a scaldare il metallo'. Fa in tempo." (f1 tr.TauMetal))
        para sb "  " (sprintf "QUELLO CHE PROTEGGE E' L'ACQUA, E QUANTA NE RESTA DIPENDE DA COSA SI E' ROTTO. Sono due scenari diversi e vanno tenuti separati. Se manca l'ACQUA ALIMENTO ma la circolazione continua a funzionare, i downcomer scendono per gravita' e si consuma tutto l'inventario: %s kg a mantello piu' %s kg nel corpo cilindrico, cioe' %s minuti. E' un margine confortevole, e spiega perche' il basso livello nel corpo cilindrico e' una protezione efficace: se ne accorge con molto anticipo." (f0 tr.WaterInventory) (f0 tr.DrumInventory) (f1 (tr.TimeToDryout / 60.0)))
        para sb "  " (sprintf "SE INVECE SI BLOCCA LA CIRCOLAZIONE - downcomer ostruiti, sacca di vapore in un riser - il corpo cilindrico non reintegra piu' e resta la sola acqua gia' presente a mantello: %s minuti. E' il caso severo, ed e' anche il piu' insidioso, perche' il livello nel corpo cilindrico NON scende e la protezione di basso livello non interviene. La grandezza che lo rivela e' la differenza di temperatura fra mantello e corpo cilindrico, non il livello." (f1 (tr.TimeToDryoutIsolated / 60.0)))
        para sb "  " (sprintf "COSA SUCCEDE DOPO. Rimasto il solo vapore a raffreddare, il coefficiente di scambio crolla di oltre un ordine di grandezza e il metallo tende verso %s °C, molto oltre il limite di %s °C del materiale, in una manciata di secondi. Non e' un danneggiamento progressivo: e' rottura per scoppio in tempi brevi. E' la ragione per cui la protezione di basso livello nel corpo cilindrico e' un blocco e non un allarme." (f0 (kToC tr.TMetalDryout)) (f0 c.Material.TmaxDesign))
        para sb "  " "AVVIAMENTO. Il caso severo per la meccanica non e' l'esercizio ma l'apparecchio CALDO E NON IN PRESSIONE, perche' manca il carico di estremita' che in esercizio annulla la compressione da dilatazione impedita: e' la condizione LC2 della sezione 8e. In pratica: pressurizzare prima di scaldare, e non lasciare l'apparecchio caldo depressurizzato piu' del necessario."
        para sb "  " "AVVERTENZA SUL MODELLO. Questi sono bilanci di primo ordine, non una simulazione dinamica. Servono a dimensionare i tempi di intervento e a dire quali protezioni devono essere blocchi; il transitorio reale, con la portata residua che decade gradualmente invece di annullarsi, richiede un modello dinamico dedicato."

        hdr sb "7. Idraulica lato mantello"
        kv sb "Area libera crossflow (banda centrale)"
            (sprintf "%s m2/m" (f4 ((r.Bands |> List.maxBy (fun b -> b.FieldFreeArea)).FieldFreeArea)))
        kv sb "Area canali liberi (media)" (sprintf "%s m2/m" (f4 (Bundle.meanBypassArea r.Bands)))
        kv sb "Ranghi attraversati (totale)" (f1 (r.Bands |> List.sumBy (fun b -> b.Rows)))
        kv sb "Vel. liquido ingresso fascio (max/min)"
            (sprintf "%s / %s m/s" (f3 (r.Axial |> List.map (fun a -> a.VelLiqIn) |> List.max))
                 (f3 (r.Axial |> List.map (fun a -> a.VelLiqIn) |> List.min)))
        kv sb "Vel. miscela uscita fascio (max)" (sprintf "%s m/s" (f2 (r.Axial |> List.map (fun a -> a.VelMixOut) |> List.max)))
        kv sb "Vel. fase vapore uscita fascio (max)" (sprintf "%s m/s" (f2 (r.Axial |> List.map (fun a -> a.VelVapOut) |> List.max)))
        kv sb "Vel. assiale plenum inferiore (max)" (sprintf "%s m/s" (f2 (r.Axial |> List.map (fun a -> a.VelAxialBottom) |> List.max)))
        kv sb "Vel. assiale plenum superiore (max)" (sprintf "%s m/s" (f2 (r.Axial |> List.map (fun a -> a.VelAxialTop) |> List.max)))
        kv sb "Titolo max in uscita dal fascio" (f4 (r.Axial |> List.map (fun a -> a.XTop) |> List.max))
        kv sb "Frazione di vuoto max (cella)" (f3 (cells |> List.map (fun x -> x.Alpha) |> List.max))

        para sb "  " "IN PAROLE SEMPLICI. Come si muove l'acqua DENTRO il mantello. Il fascio e' attraversato dal basso verso l'alto: 'area libera di crossflow' e' lo spazio che resta fra un tubo e l'altro per far passare l'acqua, e i 'ranghi attraversati' sono quante file di tubi l'acqua deve scavalcare per arrivare in cima. Le velocita' dicono se il lavaggio dei tubi e' vigoroso o pigro: troppo bassa e i tubi non si raffreddano bene, troppo alta e si rischiano vibrazioni ed erosione. La velocita' della fase vapore e' piu' alta di quella della miscela perche' il vapore, essendo leggero, scivola verso l'alto piu' veloce del liquido. Le velocita' assiali sono quelle nei due corridoi liberi sopra e sotto il fascio, dove l'acqua viaggia in lunghezza per andare dai bocchelli alla zona che deve servire."
        sb.AppendLine() |> ignore

        hdr sb "8. Bocchelli"
        for nz in r.Nozzles do
            sb.AppendLine(sprintf "  %s" nz.Service) |> ignore
            sb.AppendLine(sprintf "    numero / diametro ... %d x %s (ID %s mm)" nz.Count nz.Nps (f1 (nz.Id * 1000.0))) |> ignore
            sb.AppendLine(sprintf "    velocita' / rho*v2 .. %s m/s / %s kg/(m s2)" (f2 nz.Velocity) (f0 nz.RhoV2)) |> ignore
            sb.AppendLine(sprintf "    posizioni assiali ... %s m" (nz.Positions |> List.map f2 |> String.concat " ; ")) |> ignore
            sb.AppendLine(sprintf "    %s" nz.Note) |> ignore
            sb.AppendLine() |> ignore
        para sb "  " "IN PAROLE SEMPLICI. I bocchelli sono i fori sul mantello a cui si attaccano le tubazioni del circuito. I RISER stanno sul cielo e portano fuori la miscela con il vapore: si mettono dove il vapore viene prodotto, cioe' piu' fitti verso l'estremita' calda, e sono i piu' grandi perche' la miscela e' leggera e occupa molto volume. I DOWNCOMER stanno sul fondo e riportano dentro l'acqua: si mettono sfalsati, a meta' fra due riser, cosi' l'acqua entra dove i riser non stanno gia' estraendo. Il BLOWDOWN e' lo scarico di fondo, all'estremita' fredda e nel punto piu' basso, da cui si spurga in continuo una piccola portata per non far concentrare i sali. rho*v2 e' la 'spinta' del fluido che esce dal bocchello: si limita per non erodere il fascio e per non innescare vibrazioni."
        sb.AppendLine() |> ignore

        hdr sb "8b. Dilatazioni termiche assiali"
        sb.AppendLine("  Criterio:  dL = alpha(T_eq) * (T_eq - 20 °C) * L,  con T_eq temperatura uniforme") |> ignore
        sb.AppendLine("  equivalente che produce la stessa dilatazione del profilo reale. La temperatura") |> ignore
        sb.AppendLine("  di ogni sezione e' la MEDIA SULLO SPESSORE pesata sull'area (e' quella che governa") |> ignore
        sb.AppendLine("  la dilatazione assiale, non la temperatura di superficie).") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  elemento                                          T_eq[C]  alpha[1e-6/C]   L[m]    dL[mm]") |> ignore
        sb.AppendLine(line) |> ignore
        for e in r.Expansions do
            if Double.IsNaN e.TEquivalent then
                sb.AppendLine(sprintf "  %-48s %8s %14s %6s %9s" e.Label "-" "-" (f3 e.Length) (f2 (e.DeltaL * 1000.0))) |> ignore
            else
                sb.AppendLine(
                    sprintf "  %-48s %8s %14s %6s %9s"
                        e.Label (f1 (kToC e.TEquivalent)) (f2 (e.AlphaMean * 1e6))
                        (f3 e.Length) (f2 (e.DeltaL * 1000.0))) |> ignore
        sb.AppendLine(line) |> ignore
        legend sb
            [ "elemento", "Componente considerato. Per i tubi si riportano le bande con allungamento MASSIMO (una per classe di ferrula) e quella con allungamento MINIMO, perche' e' la differenza fra loro a generare tensioni interne al fascio, piu' la media pesata su tutti i tubi, che e' il valore da usare per il bilancio globale fascio/mantello."
              "T_eq [C]", "Temperatura uniforme EQUIVALENTE: la temperatura costante che produrrebbe la stessa dilatazione del profilo reale. Si ottiene invertendo alpha(T_eq)*(T_eq-20)*L = dL."
              "alpha [1e-6/C]", "Coefficiente MEDIO di dilatazione fra 20 C e T_eq, valutato sul materiale del componente."
              "L [m]", "Lunghezza dell'elemento (fra le facce interne delle piastre tubiere)."
              "dL [mm]", "Allungamento assiale rispetto alla condizione di montaggio a 20 C." ]
        sb.AppendLine("  Le righe DIFFERENZIALE sono le grandezze di progetto: quella tubo-mantello e' il") |> ignore
        sb.AppendLine("  carico che si scarica sulla piastra tubiera in costruzione a piastre fisse e decide") |> ignore
        sb.AppendLine("  se serve un giunto di dilatazione sul mantello; quella fra tubi genera invece") |> ignore
        sb.AppendLine("  tensioni interne al fascio ed e' di norma trascurabile perche' tutti i tubi restano") |> ignore
        sb.AppendLine("  vicini alla temperatura di saturazione.") |> ignore

        hdr sb "8c. Dilatazione impedita fascio/mantello (screening a piastre fisse)"
        let ft = r.FixedTubesheet
        kv sb "Temperatura media equiv. FASCIO (pesata)" (sprintf "%s °C" (f1 (kToC ft.TTubeMeanEq)))
        kv sb "Temperatura media equiv. tubo piu' caldo" (sprintf "%s °C" (f1 (kToC ft.TTubeHotEq)))
        kv sb "Temperatura media equiv. MANTELLO" (sprintf "%s °C" (f1 (kToC ft.TShellEq)))
        kv sb "Materiale mantello" ft.ShellMaterial
        kv sb "alpha fascio / mantello" (sprintf "%s / %s  1e-6/°C" (f2 (ft.AlphaTube * 1e6)) (f2 (ft.AlphaShell * 1e6)))
        kv sb "E fascio / mantello" (sprintf "%s / %s GPa" (f0 (ft.ETube / 1e9)) (f0 (ft.EShell / 1e9)))
        kv sb "Sezione metallica tubi / mantello" (sprintf "%s / %s m2" (f4 ft.AreaTube) (f4 ft.AreaShell))
        kv sb "DILATAZIONE DIFFERENZIALE LIBERA" (sprintf "%s mm" (f2 (ft.DeltaFree * 1000.0)))
        kv sb "FORZA ASSIALE INTERNA" (sprintf "%s MN" (f2 (ft.Force / 1e6)))
        kv sb "  tensione nei TUBI (compressione)" (sprintf "%s MPa" (f1 (ft.SigmaTube / 1e6)))
        kv sb "  tensione nel MANTELLO (trazione)" (sprintf "%s MPa" (f1 (ft.SigmaShell / 1e6)))
        kv sb "  CARICO PER TUBO sulla giunzione tubo-piastra" (sprintf "%s kN" (f1 (ft.ForcePerTube / 1000.0)))
        kv sb "Campata non supportata (passo diaframmi)" (sprintf "%s m" (f2 ft.UnsupportedSpan))
        kv sb "Raggio d'inerzia / snellezza (k = 0.5)" (sprintf "%s mm / %s" (f2 (ft.RadiusGyration * 1000.0)) (f1 ft.Slenderness))
        kv sb "Tensione ammissibile a instabilita'" (sprintf "%s MPa" (f0 (ft.SigmaBucklingAllow / 1e6)))
        kv sb "UTILIZZO a instabilita'" (sprintf "%s %%" (f0 (100.0 * ft.BucklingUtilisation)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " (sprintf "In una costruzione a PIASTRE FISSE i tubi e il mantello sono saldati alle stesse due piastre: non possono allungarsi ognuno per conto suo. Qui i tubi vorrebbero allungarsi di %s mm e il mantello di %s mm, perche' i tubi stanno mediamente a %s °C e il mantello a %s °C (il mantello e' bagnato dall'acqua che bolle, quindi sta praticamente a Tsat). La differenza, %s mm, non puo' realizzarsi: si trasforma in una forza interna." (f2 ((r.Expansions |> List.find (fun e -> e.Label.StartsWith "Tubi - MEDIA")).DeltaL * 1000.0)) (f2 ((r.Expansions |> List.find (fun e -> e.Label.StartsWith "MANTELLO")).DeltaL * 1000.0)) (f1 (kToC ft.TTubeMeanEq)) (f1 (kToC ft.TShellEq)) (f2 (ft.DeltaFree * 1000.0)))
        para sb "  " (sprintf "Tubi e mantello lavorano come due molle in parallelo: la forza si ripartisce in proporzione alle rigidezze (sezione x modulo elastico). Risultato: %s MN di forza assiale, che mette i TUBI IN COMPRESSIONE a %s MPa e il MANTELLO IN TRAZIONE a %s MPa. La stessa forza divisa per gli %d tubi da' %s kN che tirano su ogni singola giunzione tubo-piastra: e' il carico da confrontare con la tenuta della mandrinatura (o della saldatura) secondo ASME UW-20." (f2 (ft.Force / 1e6)) (f1 (ft.SigmaTube / 1e6)) (f1 (ft.SigmaShell / 1e6)) c.Tube.NTubes (f1 (ft.ForcePerTube / 1000.0)))
        para sb "  " (sprintf "I tubi in compressione possono INSTABILIZZARSI (buckling), cioe' incurvarsi fra due diaframmi come una colonna troppo snella. Con una campata di %s m la snellezza vale %s e la tensione ammissibile %s MPa: l'utilizzo e' del %s%%. %s" (f2 ft.UnsupportedSpan) (f1 ft.Slenderness) (f0 (ft.SigmaBucklingAllow / 1e6)) (f0 (100.0 * ft.BucklingUtilisation)) (if ft.BucklingUtilisation < 0.5 then "Margine ampio: i diaframmi sono abbastanza fitti." elif ft.BucklingUtilisation < 1.0 then "Margine ridotto: verificare con il calcolo di codice." else "VERIFICA NON SODDISFATTA: infittire i diaframmi o prevedere un giunto di dilatazione."))
        para sb "  " "QUALI TEMPERATURE USARE. Per il fascio si usa la temperatura media equivalente PESATA SUL NUMERO DI TUBI (tutti i tubi condividono le stesse piastre, quindi conta la media), calcolata sulla media dello spessore di ogni sezione. Il tubo piu' caldo serve invece per la verifica locale del singolo giunto. Per il mantello si usa la temperatura del metallo, che qui coincide praticamente con la saturazione perche' la lamiera e' bagnata all'interno e coibentata all'esterno."
        para sb "  " "LIMITI DI QUESTO CALCOLO. E' uno SCREENING: assume piastre infinitamente rigide, trascura i termini di pressione (lato mantello e lato tubi) e l'eventuale giunto di dilatazione. Il calcolo di codice e' TEMA RCB-7.16 oppure ASME VIII-1 UHX-13, che vanno usati per la verifica formale. I valori di E, Sy e alpha sono indicativi: per il calcolo di codice servono quelli di ASME II-D."
        sb.AppendLine("  " + String('-', 92)) |> ignore

        hdr sb "8e. Stato di sollecitazione combinato (Lame' + carico assiale)"
        let st = r.Stress
        kv sb "Pressione lato mantello (esterna ai tubi)" (sprintf "%s bar" (f2 (st.PShell / 1e5)))
        kv sb "Pressione media lato gas (interna ai tubi)" (sprintf "%s bar" (f2 (st.PTubeMean / 1e5)))
        kv sb "PRESSIONE ESTERNA NETTA sui tubi" (sprintf "%s bar" (f2 ((st.PShell - st.PTubeMean) / 1e5)))
        kv sb "Area fluida lato mantello / lato tubi" (sprintf "%s / %s m2" (f4 st.AreaFluidShell) (f4 st.AreaFluidTube))
        kv sb "CARICO DI ESTREMITA' DA PRESSIONE (trazione)" (sprintf "%s MN" (f2 (st.PressureEndLoad / 1e6)))
        kv sb "Allungamento comune imposto dalle piastre" (sprintf "%s mm" (f2 (st.CommonDelta * 1000.0)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  RIPARTIZIONE DELLA FORZA ASSIALE FRA I MEMBRI (molle in parallelo fra le due piastre)") |> ignore
        sb.AppendLine("  membro                                              n     A[m2]   E[GPa]  Teq[C]  dL lib[mm]   F[MN]  sZ tot  sZ term  sZ press [MPa]") |> ignore
        sb.AppendLine(line) |> ignore
        let memShown =
            let tubes = st.Members |> List.filter (fun m -> m.Label.StartsWith "Tubi")
            let others = st.Members |> List.filter (fun m -> not (m.Label.StartsWith "Tubi"))
            (if tubes.IsEmpty then []
             else
                [ tubes |> List.minBy (fun m -> m.SigmaZ)
                  tubes |> List.maxBy (fun m -> m.SigmaZ) ] |> List.distinctBy (fun m -> m.Label))
            @ others
        for m in memShown do
            sb.AppendLine(
                sprintf "  %-50s %5s %9s %8s %7s %11s %7s %7s %8s %8s"
                    (if m.Label.Length > 50 then m.Label.Substring(0, 50) else m.Label)
                    (f0 m.Count) (f4 m.Area) (f0 (m.E / 1e9)) (f1 (kToC m.TEq))
                    (f2 (m.FreeElongation * 1000.0)) (f2 (m.Force / 1e6))
                    (f1 (m.SigmaZ / 1e6)) (f1 (m.SigmaZThermal / 1e6)) (f1 (m.SigmaZPressure / 1e6))) |> ignore
        sb.AppendLine(line) |> ignore
        sb.AppendLine(sprintf "  (mostrati gli estremi dei %d gruppi banda x classe; il dettaglio completo e' in tensioni.csv)"
                          (st.Members |> List.filter (fun m -> m.Label.StartsWith "Tubi") |> List.length)) |> ignore
        legend sb
            [ "n", "Numero di tubi rappresentati dal gruppo (il mantello e il tubo di by-pass valgono 1)."
              "A [m2]", "Sezione METALLICA totale del membro: e' quella che porta la forza assiale."
              "E [GPa]", "Modulo elastico alla temperatura media equivalente del membro."
              "Teq [C]", "Temperatura media equivalente: quella uniforme che darebbe la stessa dilatazione assiale del profilo reale."
              "dL lib [mm]", "Allungamento che il membro avrebbe se fosse LIBERO di dilatarsi."
              "F [MN]", "Forza assiale effettiva. POSITIVA = trazione, NEGATIVA = compressione."
              "sZ tot", "Tensione assiale membranale = F/A."
              "sZ term", "Quota dovuta al solo vincolo di dilatazione (i membri piu' caldi vanno in compressione, i piu' freddi in trazione)."
              "sZ press", "Quota dovuta al carico di estremita' di pressione, che e' di TRAZIONE per tutti e si ripartisce in proporzione alla rigidezza." ]

        sb.AppendLine("  STATO TENSIONALE NELLE VARIE ZONE E ALTEZZE - punti peggiori") |> ignore
        sb.AppendLine("  comp.    z[m]    y[m]  banda  Tint[C] Test[C] dT[K]  pos.   sR    sTh    sZ   sVM  Tresca   Sy   uso[%]") |> ignore
        sb.AppendLine(line) |> ignore
        let worstCells =
            let byComp =
                st.Cells
                |> List.groupBy (fun c -> c.Component)
                |> List.collect (fun (_, cs) ->
                    (cs |> List.groupBy (fun c -> c.J) |> List.map (fun (_, g) -> g |> List.maxBy (fun c -> c.Utilisation)))
                    @ [ cs |> List.maxBy (fun c -> c.Utilisation) ])
            byComp |> List.distinctBy (fun c -> (c.Component, c.I, c.J, c.C)) |> List.sortByDescending (fun c -> c.Utilisation)
        for c in worstCells do
            let p = c.Points |> List.maxBy (fun p -> p.SigmaVM)
            sb.AppendLine(
                sprintf "  %-7s %6s %7s %5s %8s %7s %6s %-7s %5s %6s %6s %5s %6s %6s %6s"
                    c.Component (f2 c.Z) (f2 c.Y) (if c.J >= 0 then string c.J else "-")
                    (f0 (kToC c.TMetalIn)) (f0 (kToC c.TMetalOut)) (f1 c.DTWall) p.Position
                    (f1 (p.SigmaR / 1e6)) (f1 (p.SigmaTheta / 1e6)) (f1 (p.SigmaZ / 1e6))
                    (f1 (p.SigmaVM / 1e6)) (f1 (p.SigmaTresca / 1e6)) (f0 (c.Sy / 1e6))
                    (f0 (100.0 * c.Utilisation))) |> ignore
        sb.AppendLine(line) |> ignore
        legend sb
            [ "comp.", "Componente: TUBI = tubo scambiatore; BY-PASS = tubo di contenimento del by-pass centrale."
              "z [m]", "Posizione lungo l'asse dell'apparecchio, dalla piastra tubiera lato gas caldo."
              "y [m]", "Altezza della banda rispetto all'asse del mantello (positiva verso l'alto)."
              "dT [K]", "Salto di temperatura NELLO SPESSORE del tubo: e' quello che genera le tensioni secondarie di gradiente termico."
              "pos.", "Punto radiale in cui si verifica la tensione equivalente massima: faccia interna, media o esterna."
              "sR [MPa]", "Tensione RADIALE (Lame' + gradiente termico). Alla faccia interna vale -p_interna, all'esterna -p_esterna."
              "sTh [MPa]", "Tensione CIRCONFERENZIALE (di cerchio). Con pressione esterna prevalente e' di COMPRESSIONE (negativa)."
              "sZ [MPa]", "Tensione ASSIALE totale: membranale (vincolo termico + carico di estremita' di pressione) piu' il termine di gradiente termico."
              "sVM [MPa]", "Tensione equivalente di VON MISES, sqrt(0.5[(s1-s2)^2+(s2-s3)^2+(s3-s1)^2]): il criterio usato per confrontare uno stato triassiale con una prova di trazione."
              "Tresca", "Tensione equivalente di TRESCA = tensione principale massima meno minima. E' il criterio usato da ASME; e' sempre >= von Mises."
              "Sy [MPa]", "Snervamento del materiale ALLA TEMPERATURA LOCALE del metallo."
              "uso [%]", "sVM / Sy: percentuale dello snervamento utilizzata." ]

        sb.AppendLine("  VERIFICHE DI INSTABILITA' (compressione assiale e pressione esterna)") |> ignore
        sb.AppendLine("  elemento                                        s comp[MPa] campata[m] snellez.  ammiss[MPa] uso[%]  p_ext[bar] p_coll[bar] uso[%]") |> ignore
        sb.AppendLine(line) |> ignore
        for b in st.Bucklings do
            sb.AppendLine(
                sprintf "  %-46s %11s %10s %8s %12s %6s %11s %11s %6s"
                    (if b.Label.Length > 46 then b.Label.Substring(0, 46) else b.Label)
                    (f1 (b.SigmaCompression / 1e6)) (f2 b.Span) (f1 b.Slenderness)
                    (f0 (b.SigmaAllow / 1e6)) (f0 (100.0 * b.Utilisation))
                    (f1 (b.PExtNet / 1e5)) (f0 (b.PCollapse / 1e5)) (f0 (100.0 * b.CollapseUtil))) |> ignore
        sb.AppendLine(line) |> ignore
        for b in st.Bucklings do
            sb.AppendLine(sprintf "    %-46s %s" b.Label b.Note) |> ignore
        legend sb
            [ "s comp", "Tensione assiale di COMPRESSIONE effettiva (zero se l'elemento e' in trazione)."
              "campata", "Distanza fra due supporti consecutivi (diaframmi): e' la lunghezza libera di inflessione."
              "snellez.", "Snellezza = 0.5 * campata / raggio d'inerzia. Il fattore 0.5 vale per estremi incastrati, come sono i tubi nei diaframmi."
              "ammiss", "Tensione ammissibile a instabilita' di colonna, metodo tipo AISC/TEMA."
              "p_ext", "Pressione esterna NETTA sul cilindro = pressione a mantello meno pressione interna."
              "p_coll", "Pressione di collasso stimata: combinazione del collasso elastico dell'anello lungo, 2E/(1-nu^2)(t/Dm)^3, e dello snervamento circonferenziale, 2 Sy t/Do." ]

        if not (Double.IsNaN st.LinerTEq) then
            kv sb "LINER DEL BY-PASS: T media equivalente" (sprintf "%s °C" (f1 (kToC st.LinerTEq)))
            kv sb "  allungamento LIBERO del liner" (sprintf "%s mm" (f1 (st.LinerFreeElongation * 1000.0)))
            kv sb "  vincolo" "LIBERO di dilatare (dato costruttivo confermato): nessun carico assiale"
            kv sb "  forza nell'IPOTESI CONTRARIA (non applicabile)" (sprintf "%s MN, cioe' l'ordine di grandezza che il giunto scorrevole evita" (f2 (st.LinerRestrainedForce / 1e6)))

        sb.AppendLine() |> ignore
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " "CHE COSA SOMMA QUESTA SEZIONE. Un tubo di questo apparecchio e' contemporaneamente: (1) schiacciato dall'esterno, perche' l'acqua a mantello sta a pressione molto piu' alta del gas che ha dentro; (2) compresso o tirato in direzione assiale, perche' le due piastre gli impediscono di allungarsi come vorrebbe; (3) attraversato da un salto di temperatura nello spessore, che fa litigare la faccia calda con quella fredda. Le tre cose si sommano nello stesso punto di metallo, e solo la somma dice se il tubo e' verificato."
        para sb "  " "LE FORMULE DI LAME'. Sono la soluzione esatta di un cilindro di parete spessa premuto da dentro e da fuori. Danno due tensioni: la RADIALE, che alle due facce vale semplicemente meno la pressione che agisce li'; e la CIRCONFERENZIALE, quella che tende ad aprire o chiudere il cerchio. Nel caso classico della caldaia a tubi d'acqua la pressione e' dentro e la circonferenziale e' di trazione. Qui e' il contrario: la pressione grande e' FUORI, quindi il cerchio e' COMPRESSO. Non e' un dettaglio, perche' cambia il modo in cui il tubo puo' rompersi: non si apre, si schiaccia."
        para sb "  " (sprintf "IL CARICO ASSIALE, E PERCHE' NON E' SOLO TERMICO. La dilatazione impedita da' compressione ai tubi (sono piu' caldi del mantello). Ma la pressione, spingendo sulle piastre e sui fondi, TIRA tutto l'apparecchio: e' un carico di estremita' di %s MN che si ripartisce fra tubi e mantello in proporzione alle rigidezze, e che in gran parte CANCELLA la compressione termica. E' il motivo per cui la tensione assiale finale nei tubi e' molto piu' bassa di quella che si otterrebbe guardando solo la dilatazione. Trascurarlo, come fa lo screening della sezione 8c, e' conservativo per il buckling ma non per la giunzione tubo-piastra." (f2 (st.PressureEndLoad / 1e6)))
        para sb "  " "IL GRADIENTE TERMICO NELLO SPESSORE. La faccia interna e' piu' calda: vorrebbe allungarsi di piu', ma il resto del metallo glielo impedisce. Risultato: la faccia calda va in COMPRESSIONE, quella fredda in trazione, con valore limite alpha*E*dT/(2(1-nu)). Sono tensioni SECONDARIE, cioe' autoequilibrate: non fanno collassare il tubo, ma sono quelle che contano per la FATICA TERMICA agli avviamenti e ai transitori, e per il rilassamento a caldo. Per questo vanno riportate separatamente e non semplicemente sommate ai limiti di membrana."
        para sb "  " "PERCHE' VON MISES E TRESCA. Uno stato di tensione a tre componenti non si confronta direttamente con lo snervamento, che si misura tirando una provetta in una direzione sola. Servono criteri di equivalenza: von Mises pesa le differenze fra le tre tensioni principali, Tresca guarda solo la differenza fra la massima e la minima. Tresca e' sempre piu' severo (al massimo del 15%), ed e' quello adottato dal codice ASME. Sono riportati entrambi."
        para sb "  " "ZONE E ALTEZZE. La tensione assiale membranale e' costante lungo il tubo (lo impone l'equilibrio) ma cambia da banda a banda, perche' i tubi delle bande piu' calde sono piu' compressi. Le tensioni di pressione e di gradiente termico cambiano invece lungo z, perche' cambiano la pressione del gas e il salto nello spessore. Il punto peggiore e' quindi l'incrocio fra una banda calda e la zona di picco del flusso termico, subito a valle della ferrula."
        para sb "  " "IL LINER DEL BY-PASS E' UN CASO A PARTE, ED E' RISOLTO. Lavora a temperatura molto piu' alta di tutto il resto e non porta pressione, perche' dentro e fuori ha lo stesso gas. Costruttivamente e' LIBERO DI DILATARE: non sviluppa quindi alcun carico assiale e non compare fra i membri del sistema a piastre fisse, dove figura il solo tubo di contenimento. La forza riportata sopra e' l'ipotesi contraria - liner vincolato a entrambe le estremita' - ed e' riportata solo per documentare l'ordine di grandezza di cio' che il giunto scorrevole evita: un valore che nessuna costruzione reggerebbe. E' la ragione costruttiva per cui quel giunto esiste."
        sb.AppendLine("  " + String('-', 92)) |> ignore

        hdr sb "8f. Ripartizione delle spinte assiali di pressione"
        let stT = r.Stress
        let tubeMem = stT.Members |> List.filter (fun m -> m.Label.StartsWith "Tubi")
        let shellMem = stT.Members |> List.find (fun m -> m.Label = "MANTELLO")
        let bpMem = stT.Members |> List.tryFind (fun m -> m.Label.StartsWith "BY-PASS")
        let fTubes = tubeMem |> List.sumBy (fun m -> m.Force)
        let fTubesP = tubeMem |> List.sumBy (fun m -> m.SigmaZPressure * m.Area)
        let fTubesT = tubeMem |> List.sumBy (fun m -> m.SigmaZThermal * m.Area)
        let aTubes = tubeMem |> List.sumBy (fun m -> m.Area)
        let nT = float c.Tube.NTubes
        para sb "  " "DA DOVE NASCE LA SPINTA. Si taglia l'apparecchio con un piano in mezzeria e si isola tutto quello che sta a monte: fondo, piastra tubiera, meta' mantello, meta' tubi e il fluido contenuto. Sull'esterno agisce l'atmosfera, cioe' zero in pressione relativa. Al taglio agiscono le forze del metallo e le pressioni dei fluidi sulle rispettive sezioni fluide. L'equilibrio da':"
        sb.AppendLine() |> ignore
        sb.AppendLine("      F_metallo = p_mantello x A_fluido,mantello  +  p_tubi x A_fluido,tubi") |> ignore
        sb.AppendLine() |> ignore
        para sb "  " "E' una TRAZIONE: la pressione tende ad allungare l'apparecchio, sempre, indipendentemente da quale dei due lati sia il piu' spinto. Non e' una differenza di pressioni, e' una somma di due contributi."
        sb.AppendLine() |> ignore
        kv sb "p a mantello / area fluida a mantello" (sprintf "%s bar  x  %s m2" (f2 (stT.PShell / 1e5)) (f4 stT.AreaFluidShell))
        kv sb "p nei tubi / area fluida nei tubi" (sprintf "%s bar  x  %s m2" (f2 (stT.PTubeMean / 1e5)) (f4 stT.AreaFluidTube))
        kv sb "  contributo del lato mantello" (sprintf "%s MN" (f2 (stT.PShell * stT.AreaFluidShell / 1e6)))
        kv sb "  contributo del lato tubi" (sprintf "%s MN" (f2 (stT.PTubeMean * stT.AreaFluidTube / 1e6)))
        kv sb "SPINTA ASSIALE TOTALE DI PRESSIONE" (sprintf "%s MN in TRAZIONE" (f2 (stT.PressureEndLoad / 1e6)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  COME SI RIPARTISCE FRA I TRE ELEMENTI (in proporzione alla rigidezza assiale A*E/L)") |> ignore
        sb.AppendLine("  elemento                          A [m2]   E[GPa]   rigidezza    quota    F press.   sigma press.") |> ignore
        sb.AppendLine("                                                       [MN/mm]      [%]        [MN]        [MPa]") |> ignore
        sb.AppendLine(line) |> ignore
        let kOf (a: float) (e: float) = a * e / c.Tube.Length
        let kTot =
            kOf aTubes (tubeMem |> List.averageBy (fun m -> m.E))
            + kOf shellMem.Area shellMem.E
            + (match bpMem with Some m -> kOf m.Area m.E | None -> 0.0)
        let row (nm: string) (a: float) (e: float) (fp: float) =
            sb.AppendLine(
                sprintf "  %-32s %8s %8s %11s %8s %11s %12s"
                    nm (f4 a) (f0 (e / 1e9)) (f3 (kOf a e / 1e9)) (f1 (100.0 * kOf a e / kTot))
                    (f3 (fp / 1e6)) (f1 (fp / a / 1e6))) |> ignore
        row (sprintf "FASCIO TUBIERO (%d tubi)" c.Tube.NTubes) aTubes (tubeMem |> List.averageBy (fun m -> m.E)) fTubesP
        row "VIROLA DI MANTELLO" shellMem.Area shellMem.E (shellMem.SigmaZPressure * shellMem.Area)
        (match bpMem with
         | Some m -> row "TUBO DI CONTENIMENTO BY-PASS" m.Area m.E (m.SigmaZPressure * m.Area)
         | None -> ())
        sb.AppendLine(line) |> ignore
        kv sb "SPINTA DI PRESSIONE SUL SINGOLO TUBO" (sprintf "%s kN in TRAZIONE" (f2 (fTubesP / nT / 1000.0)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  CONFRONTO CON LA DILATAZIONE IMPEDITA: SI ANNULLANO O SI SOMMANO?") |> ignore
        sb.AppendLine("  elemento                        da PRESSIONE   da DILATAZIONE   RISULTANTE   effetto") |> ignore
        sb.AppendLine("                                        [MPa]            [MPa]        [MPa]") |> ignore
        sb.AppendLine(line) |> ignore
        let cmp (nm: string) (sp: float) (st2: float) =
            let tot = sp + st2
            let eff =
                if sp * st2 < 0.0 then
                    (if abs tot < abs sp then "SI ANNULLANO in parte" else "si oppongono")
                else "SI SOMMANO"
            sb.AppendLine(
                sprintf "  %-32s %13s %16s %12s   %s  (%s)"
                    nm (f1 (sp / 1e6)) (f1 (st2 / 1e6)) (f1 (tot / 1e6)) eff
                    (if tot > 0.0 then "trazione" else "compressione")) |> ignore
        cmp (sprintf "FASCIO TUBIERO (media %d tubi)" c.Tube.NTubes) (fTubesP / aTubes) (fTubesT / aTubes)
        cmp "VIROLA DI MANTELLO" shellMem.SigmaZPressure shellMem.SigmaZThermal
        (match bpMem with
         | Some m -> cmp "TUBO DI CONTENIMENTO BY-PASS" m.SigmaZPressure m.SigmaZThermal
         | None -> ())
        sb.AppendLine(line) |> ignore
        kv sb "CARICO NETTO SUL SINGOLO TUBO" (sprintf "%s kN  (%s)" (f2 (fTubes / nT / 1000.0)) (if fTubes > 0.0 then "TRAZIONE - il giunto tubo-piastra e' TIRATO" else "COMPRESSIONE"))
        kv sb "  di cui da pressione" (sprintf "%s kN in trazione" (f2 (fTubesP / nT / 1000.0)))
        kv sb "  di cui da dilatazione impedita" (sprintf "%s kN in %s" (f2 (abs fTubesT / nT / 1000.0)) (if fTubesT < 0.0 then "compressione" else "trazione"))
        legend sb
            [ "A [m2]", "Sezione METALLICA dell'elemento: 848 volte la corona circolare per il fascio, la corona della virola per il mantello."
              "rigidezza", "A*E/L: quanto l'elemento si oppone all'allungamento. La spinta si ripartisce in questa proporzione perche' le piastre impongono a tutti lo STESSO allungamento."
              "quota [%]", "Frazione della spinta totale che quell'elemento si prende."
              "F press.", "Forza di trazione che la pressione mette in quell'elemento."
              "da PRESSIONE", "Tensione assiale dovuta alla spinta di pressione: sempre di TRAZIONE, positiva."
              "da DILATAZIONE", "Tensione assiale dovuta al vincolo delle piastre: negativa (compressione) per gli elementi PIU' CALDI della media, positiva per i piu' freddi."
              "RISULTANTE", "Somma algebrica: e' quello che il metallo sente davvero." ]
        sb.AppendLine("  IN PAROLE SEMPLICI") |> ignore
        sb.AppendLine("  " + String('-', 92)) |> ignore
        para sb "  " "LA DOMANDA GIUSTA E' SE SI SOMMANO O SI ANNULLANO, E LA RISPOSTA E' DIVERSA PER OGNI ELEMENTO. La pressione mette in TRAZIONE tutto quanto, senza eccezioni. La dilatazione impedita invece ridistribuisce: comprime chi e' piu' caldo della media pesata e tira chi e' piu' freddo. I tubi sono i piu' caldi, quindi la dilatazione li COMPRIME e la pressione li TIRA: i due effetti hanno segno opposto e si elidono in buona parte. Il mantello e' il piu' freddo, quindi la dilatazione lo TIRA e la pressione pure: li' i due effetti SI SOMMANO."
        para sb "  " "LA CONSEGUENZA PRATICA PIU' IMPORTANTE riguarda l'instabilita' dei tubi. Se si guardasse la sola dilatazione impedita si concluderebbe che i tubi sono compressi e vanno verificati a carico di punta. Aggiungendo la pressione la compressione si riduce fortemente o si annulla del tutto. Ma attenzione: la pressione c'e' solo quando l'apparecchio E' IN PRESSIONE. Nella condizione CALDO E NON IN PRESSIONE - avviamento, depressurizzazione a caldo - resta la sola compressione termica, senza contrasto. E' quella la condizione severa per il buckling, ed e' la LC2 riportata nella sezione 8e."
        para sb "  " "IL TIRANTAGGIO DEL GIUNTO TUBO-PIASTRA e' l'altra faccia. La forza netta per tubo indicata sopra e' quella che il giunto deve trasmettere: se e' di trazione, tende a SFILARE il tubo dalla piastra, ed e' il carico da confrontare con la tenuta della mandrinatura o della saldatura secondo ASME VIII-1 UW-20. Se e' di compressione, il tubo spinge contro la piastra e il giunto non e' sollecitato a sfilamento, ma il tubo puo' instabilizzarsi."
        para sb "  " "PERCHE' LA RIPARTIZIONE SEGUE LA RIGIDEZZA E NON L'AREA. Le due piastre sono rigide e impongono a tutti gli elementi lo stesso allungamento. Un elemento rigido, per allungarsi di quel tanto, richiede piu' forza: quindi si prende una quota maggiore della spinta. La quota e' proporzionale ad A*E/L, non alla sola area, ed e' per questo che il mantello - piu' rigido del fascio - si prende la fetta maggiore anche se la sua sezione metallica non e' molto diversa."

        hdr sb "8g. Verifica del liner del by-pass a pressione differenziale"
        let lc = stT.Liner
        para sb "  " "IL LINER NON E' UN COMPONENTE IN PRESSIONE. Separa due volumi di gas che stanno quasi alla stessa pressione: dentro c'e' il gas deviato, fuori l'intercapedine con la carta refrattaria, che comunica direttamente con il lato A VALLE del fascio. Il salto che il liner puo' vedere non e' quindi la differenza fra acqua e gas, che sarebbe di decine di bar, ma soltanto la PERDITA DI CARICO DEI TUBI, che e' di poche centinaia di millibar."
        sb.AppendLine() |> ignore
        kv sb "Perdita di carico lato tubi (salto disponibile)" (sprintf "%s mbar" (f1 (lc.DpTubes / 100.0)))
        kv sb "Fattore di maggiorazione adottato" (sprintf "x %s" (f1 lc.Factor))
        kv sb "SALTO DI PROGETTO" (sprintf "%s mbar = %s bar" (f1 (lc.DpDesign / 100.0)) (f3 (lc.DpDesign / 1e5)))
        kv sb "Geometria del liner" (sprintf "ID %s - OD %s mm, spessore %s mm (D/t = %s)" (f0 (lc.Id * 1000.0)) (f0 (lc.Od * 1000.0)) (f1 (lc.Thickness * 1000.0)) (f0 (lc.Od / lc.Thickness)))
        kv sb "Materiale / temperatura media equivalente" (sprintf "%s a %s °C" c.Bypass.LinerMaterial.Name (f0 (kToC lc.TEq)))
        kv sb "E / Sy a quella temperatura" (sprintf "%s GPa / %s MPa" (f0 (lc.E / 1e9)) (f0 (lc.Sy / 1e6)))
        sb.AppendLine() |> ignore
        sb.AppendLine("  VERIFICA A PRESSIONE ESTERNA (verso piu' severo)") |> ignore
        kv sb "  collasso elastico, cilindro lungo" (sprintf "%s bar" (f2 (lc.PCrElastic / 1e5)))
        kv sb "  cedimento plastico circonferenziale" (sprintf "%s bar" (f2 (lc.PCrYield / 1e5)))
        kv sb "  COLLASSO DI PROGETTO (minimo con interazione)" (sprintf "%s bar" (f2 (lc.PCollapse / 1e5)))
        kv sb "  UTILIZZO diretto" (sprintf "%s %%" (f1 (100.0 * lc.Utilisation)))
        kv sb "  UTILIZZO con fattore di sicurezza 3 (ASME UG-28)" (sprintf "%s %%" (f1 (100.0 * lc.UtilisationCode)))
        kv sb "Tensione circonferenziale nel verso interno" (sprintf "%s MPa su Sy %s MPa" (f2 (lc.HoopStress / 1e6)) (f0 (lc.Sy / 1e6)))
        sb.AppendLine() |> ignore
        kv sb "ESITO SPESSORE 3 mm"
            (if lc.UtilisationCode < 1.0 then sprintf "VERIFICATO - utilizzo %s %% anche con il fattore di sicurezza di codice" (f0 (100.0 * lc.UtilisationCode))
             else sprintf "NON VERIFICATO - utilizzo %s %%" (f0 (100.0 * lc.UtilisationCode)))
        for n in lc.Notes do para sb "    - " n
        legend sb
            [ "salto disponibile", "Perdita di carico del gas fra ingresso e uscita dei tubi scambiatori: e' l'unico salto che puo' stabilirsi ai capi della parete del liner."
              "fattore x2", "Maggiorazione sul salto per coprire transitori, posizione della valvola e scostamenti d'esercizio, e per assorbire l'incertezza sul verso."
              "D/t", "Rapporto diametro/spessore: misura la snellezza della parete. Sopra 50 la parete e' sottile e il collasso elastico governa sul cedimento plastico."
              "collasso elastico", "2E/(1-nu^2)(t/Dm)^3: e' l'instabilita' della parete sottile, che si accartoccia. Non dipende dal materiale se non tramite E."
              "cedimento plastico", "2 Sy t/Do: e' il cerchio che snerva in compressione."
              "utilizzo con FS 3", "ASME UG-28 richiede un fattore 3 sulla pressione di collasso: e' il criterio da confrontare con 100 %." ]
        para sb "  " "IN PAROLE SEMPLICI. Un tubo sottile di 275 mm di diametro con 3 mm di parete e' molto snello, quindi verrebbe da preoccuparsi. Ma quello che conta non e' la snellezza in assoluto: e' il rapporto fra il carico e la capacita'. Qui il carico e' minuscolo, perche' il liner non separa l'acqua dal gas ma il gas dal gas, e fra i due lati c'e' solo la perdita di carico del fascio. Il risultato e' che 3 mm bastano con un margine molto ampio anche raddoppiando il salto e applicando il fattore di sicurezza del codice."
        para sb "  " "QUELLO CHE DAVVERO DIMENSIONA IL LINER NON E' LA PRESSIONE. A quella temperatura il liner e' governato da altro: la resistenza alla carburazione e al metal dusting, che decide il materiale; la deformazione per scorrimento viscoso sotto il proprio peso e i gradienti termici nel lungo periodo; e la libera dilatazione, che decide il vincolo. Lo spessore di 3 mm e' quello che serve per fabbricarlo, saldarlo e farlo durare, non per reggere una pressione."

        hdr sb "8d. Tubazioni del circuito: distinta e perdite di carico"
        sb.AppendLine("  linea    DN                 ID[mm]  z[m]  ang[°]  L svil[m]  curve  K tot  W[kg/s]  v[m/s]  rho*v2  regime") |> ignore
        sb.AppendLine(line) |> ignore
        for lc in r.LineChecks do
            sb.AppendLine(
                sprintf "  %-8s %-18s %6s %5s %7s %10s %6d %6s %8s %7s %7s  %s"
                    lc.Tag lc.Nps (f1 (lc.Id * 1000.0)) (f2 lc.ZNozzle) (f0 lc.AngleDeg)
                    (f2 lc.DevelopedLength) lc.NElbows (f2 lc.KTotal)
                    (f1 lc.Flow) (f2 lc.Velocity) (f0 lc.RhoV2)
                    (if not lc.Connected then "*** NON COLLEGATO - escluso dal calcolo ***"
                     else match lc.Regime with Some rg -> Mechanics.regimeName rg | None -> "liquido")) |> ignore
        sb.AppendLine(line) |> ignore
        let nc = r.LineChecks |> List.filter (fun l -> not l.Connected)
        if not nc.IsEmpty then
            sb.AppendLine() |> ignore
            sb.AppendLine("  BOCCHELLI PRESENTI MA NON IN SERVIZIO") |> ignore
            for l in nc do
                sb.AppendLine(sprintf "    %-6s %-18s z = %5s m, %4s°   %s" l.Tag l.Nps (f2 l.ZNozzle) (f0 l.AngleDeg) l.Note) |> ignore
            para sb "  " (sprintf "Il calcolo idraulico e' stato eseguito SENZA queste %d linee: la sezione di passaggio e il battente motore sono quelli effettivamente disponibili, non quelli che si leggerebbero sul disegno. Attenzione a dove si trovano: %s. Sono proprio le estremita' dell'apparecchio, cioe' le zone dove il campo tubi e' meno lavato dalla circolazione trasversale." nc.Length (nc |> List.map (fun l -> sprintf "%s a z = %.2f m" l.Tag l.ZNozzle) |> String.concat "; "))
            sb.AppendLine() |> ignore
        sb.AppendLine("  DISTINTA DI OGNI LINEA (tratti diritti e curve)") |> ignore
        for lc in r.LineChecks do
            sb.AppendLine(sprintf "    %-6s %s%s" lc.Tag lc.Bom (if lc.Note = "" then "" else "  [" + lc.Note + "]")) |> ignore
        legend sb
            [ "linea", "Sigla del bocchello e della sua tubazione (R = riser, DC = downcomer)."
              "z [m]", "Posizione assiale del bocchello sul mantello, misurata dalla piastra tubiera lato gas caldo."
              "ang [°]", "Posizione angolare sulla circonferenza del mantello: 0° = cielo (riser), 180° = fondo, 150° e 210° = fianchi bassi."
              "L svil [m]", "Lunghezza SVILUPPATA della tubazione: somma dei tratti diritti piu' gli archi delle curve. E' la lunghezza che conta per l'attrito distribuito, non la distanza in linea d'aria."
              "curve", "Numero totale di curve nella linea (di qualsiasi angolo)."
              "K tot", "Coefficiente di resistenza complessivo riferito alla velocita' nella linea: attrito distribuito (f*L/D) piu' le curve (metodo di Idelchik: contributo d'angolo, di raggio e di attrito sull'arco) piu' imbocco e sbocco."
              "W [kg/s]", "Portata che passa in QUELLA linea. Non e' la stessa per tutte: le linee sono in parallelo fra gli stessi due punti, quindi la portata si ripartisce in modo che ognuna dissipi la stessa Δp. Le linee corte e larghe ne prendono di piu'."
              "v [m/s]", "Velocita' del fluido nella linea (miscela omogenea per i riser, liquido per i downcomer)."
              "rho*v2", "Quantita' di moto specifica [kg/(m s2)]: parametro per erosione e forzanti vibrazionali."
              "regime", "Regime di moto bifase nei riser (mappa di Taitel-Dukler). I downcomer sono liquido monofase." ]
        para sb "  " "IN PAROLE SEMPLICI. Questa sezione traduce il disegno delle tubazioni in numeri idraulici. Ogni linea del circuito viene descritta come nella distinta del disegno: tanti spezzoni diritti di lunghezza nota piu' tante curve di angolo e raggio noti. Da qui il programma ricava la lunghezza sviluppata reale (i tratti diritti piu' gli archi delle curve) e il coefficiente di resistenza totale, invece di usare una lunghezza equivalente stimata. Le curve non contano tutte uguale: una curva a 90° pesa circa il doppio di una a 30°, e a parita' di angolo una curva a raggio stretto pesa piu' di una a raggio largo. Poi, poiche' tutte le linee collegano gli stessi due punti (il mantello e il corpo cilindrico), la portata si distribuisce fra loro in modo che ciascuna dissipi la stessa caduta di pressione: le linee piu' corte e larghe ne prendono di piu'. E' il motivo per cui non basta sommare le sezioni per sapere quanto circola."
        sb.AppendLine() |> ignore

        hdr sb "8h. Dati per il calcolo FEA della piastra tubiera e del giunto tubo-piastra"
        sb.AppendLine("  ****************************************************************************") |> ignore
        sb.AppendLine("  *  IL CALCOLO FEA E' OBBLIGATORIO. Tutto quanto precede e' uno SCREENING  *") |> ignore
        sb.AppendLine("  *  di dimensionamento, non una verifica di codice. La piastra tubiera e   *") |> ignore
        sb.AppendLine("  *  il giunto tubo-piastra vanno verificati con analisi agli elementi      *") |> ignore
        sb.AppendLine("  *  finiti secondo ASME VIII Div.2 Parte 5, con i carichi qui riassunti.   *") |> ignore
        sb.AppendLine("  ****************************************************************************") |> ignore
        sb.AppendLine() |> ignore
        para sb "  " "Perche' non basta lo screening: il calcolo di questo report assume piastre INFINITAMENTE RIGIDE. La piastra reale e' una piastra forata che si INFLETTE, e la sua flessibilita' cambia la ripartizione dei carichi, genera flessione nei tubi vicino all'incastro e concentra le tensioni sul legamento fra foro e foro. Nessuna formula chiusa cattura questi effetti su una piastra con 848 fori e un foro centrale grande per il by-pass."
        sb.AppendLine() |> ignore
        let feaHdr (t: string) =
            sb.AppendLine() |> ignore
            sb.AppendLine("  " + t) |> ignore
            sb.AppendLine("  " + String('-', 74)) |> ignore
        let fea (k: string) (v: string) = sb.AppendLine(sprintf "    %-44s %s" k v) |> ignore
        let stF = r.Stress
        let tubeM = stF.Members |> List.filter (fun m -> m.Label.StartsWith "Tubi")
        let shellM = stF.Members |> List.find (fun m -> m.Label = "MANTELLO")
        let aTb = tubeM |> List.sumBy (fun m -> m.Area)
        let fTb = tubeM |> List.sumBy (fun m -> m.Force)
        let fTbP = tubeM |> List.sumBy (fun m -> m.SigmaZPressure * m.Area)
        let fTbT = tubeM |> List.sumBy (fun m -> m.SigmaZThermal * m.Area)
        let nTb = float c.Tube.NTubes

        feaHdr "1. GEOMETRIA"
        fea "Numero di tubi" (sprintf "%d" c.Tube.NTubes)
        fea "Tubo: OD x spessore x lunghezza" (sprintf "%s x %s x %s mm" (f1 (c.Tube.Do * 1000.0)) (f2 ((c.Tube.Do - c.Tube.Di) * 500.0)) (f0 (c.Tube.Length * 1000.0)))
        fea "Passo / reticolo" (sprintf "%s mm - %s" (f1 (c.Tube.Pitch * 1000.0)) (Vibration.layoutName c.TubeLayout))
        fea "Legamento fra fori (pitch - Do)" (sprintf "%s mm" (f2 ((c.Tube.Pitch - c.Tube.Do) * 1000.0)))
        fea "Coefficiente di foratura mu* = (p-d)/p" (sprintf "%s" (f4 ((c.Tube.Pitch - c.Tube.Do) / c.Tube.Pitch)))
        fea "OTL / ITL (anima non intubata)" (sprintf "%s / %s mm" (f1 (c.Tube.Otl * 1000.0)) (f0 (c.Tube.Itl * 1000.0)))
        fea "Mantello: ID x spessore" (sprintf "%s x %s mm" (f0 (c.Tube.ShellId * 1000.0)) (f0 (c.ShellThickness * 1000.0)))
        fea "Foro centrale per il by-pass (OD tubo)" (sprintf "%s mm" (f0 (c.Bypass.PipeOd * 1000.0)))
        fea "Campate fra i diaframmi" (sprintf "%s mm" (c.BaffleSpans |> List.map (fun x -> f0 (x * 1000.0)) |> String.concat " / "))
        fea "Spessore diaframmi / gioco foro-tubo" (sprintf "%s mm / 0.40 mm sul diametro" (f0 (c.BaffleThickness * 1000.0)))

        feaHdr "2. MATERIALI (valori alle temperature di esercizio calcolate)"
        fea "Tubi" c.Material.Name
        fea "  T media equivalente / E / Sy / alpha"
            (sprintf "%s °C  /  %s GPa  /  %s MPa  /  %s e-6 1/°C"
                 (f1 (kToC r.FixedTubesheet.TTubeMeanEq)) (f0 (r.FixedTubesheet.ETube / 1e9))
                 (f0 (c.Material.Sy (kToC r.FixedTubesheet.TTubeMeanEq) / 1e6)) (f2 (r.FixedTubesheet.AlphaTube * 1e6)))
        fea "Mantello e piastre tubiere" c.ShellMaterial.Name
        fea "  T media equivalente / E / Sy / alpha"
            (sprintf "%s °C  /  %s GPa  /  %s MPa  /  %s e-6 1/°C"
                 (f1 (kToC r.FixedTubesheet.TShellEq)) (f0 (r.FixedTubesheet.EShell / 1e9))
                 (f0 (c.ShellMaterial.Sy (kToC r.FixedTubesheet.TShellEq) / 1e6)) (f2 (r.FixedTubesheet.AlphaShell * 1e6)))
        fea "Tubo di contenimento by-pass" c.Bypass.PipeMaterial.Name
        fea "Liner by-pass (NON strutturale, libero)" c.Bypass.LinerMaterial.Name

        feaHdr "3. PRESSIONI"
        fea "Lato mantello (acqua/vapore saturi)" (sprintf "%s bar a" (f2 (stF.PShell / 1e5)))
        fea "Lato tubi, ingresso / media" (sprintf "%s / %s bar a" (f2 (paToBar c.Gas.PIn)) (f2 (stF.PTubeMean / 1e5)))
        fea "Perdita di carico lato tubi" (sprintf "%s mbar" (f1 (r.DpGas / 100.0)))
        fea "Pressione esterna netta sui tubi" (sprintf "%s bar" (f2 ((stF.PShell - stF.PTubeMean) / 1e5)))

        feaHdr "4. TEMPERATURE DA IMPORRE"
        fea "Gas ingresso / uscita miscelata" (sprintf "%s / %s °C" (f1 (kToC c.Gas.TIn)) (f1 (kToC r.TGasOutMean)))
        fea "Saturazione a mantello" (sprintf "%s °C" (f2 (kToC r.Sat.Tsat)))
        fea "Piastra lato GAS CALDO: faccia gas" (sprintf "%s °C (con ferrula in opera)" (f0 (kToC (r.Cells |> List.filter (fun x -> x.InFerrule) |> List.map (fun x -> x.TMetalIn) |> List.max))))
        fea "Piastra: faccia lato acqua" (sprintf "%s °C circa (Tsat + qualche K)" (f0 (kToC r.Sat.Tsat + 5.0)))
        fea "Tubo: T media equivalente per la dilatazione" (sprintf "%s °C" (f1 (kToC r.FixedTubesheet.TTubeMeanEq)))
        fea "Tubo: T metallica massima (interna)" (sprintf "%s °C a z = %s m" (f0 (kToC (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max))) (f2 (r.Cells |> List.filter (fun x -> not x.InFerrule) |> List.maxBy (fun x -> x.QFluxOut)).Z))
        fea "Mantello: T media equivalente" (sprintf "%s °C" (f1 (kToC r.FixedTubesheet.TShellEq)))
        fea "T di montaggio (riferimento dilatazioni)" (sprintf "%s °C" (f0 (kToC c.AssemblyTemperature)))

        feaHdr "5. CARICHI ASSIALI GIA' RIPARTITI (da imporre o da verificare)"
        fea "SPINTA DI PRESSIONE TOTALE" (sprintf "%s MN in trazione" (f2 (stF.PressureEndLoad / 1e6)))
        fea "  sul fascio tubiero" (sprintf "%s MN = %s MPa" (f3 (fTbP / 1e6)) (f1 (fTbP / aTb / 1e6)))
        fea "  sulla virola di mantello" (sprintf "%s MN = %s MPa" (f3 (shellM.SigmaZPressure * shellM.Area / 1e6)) (f1 (shellM.SigmaZPressure / 1e6)))
        fea "DILATAZIONE IMPEDITA: differenziale libero" (sprintf "%s mm" (f2 (r.FixedTubesheet.DeltaFree * 1000.0)))
        fea "  sul fascio tubiero" (sprintf "%s MN = %s MPa" (f3 (fTbT / 1e6)) (f1 (fTbT / aTb / 1e6)))
        fea "  sulla virola di mantello" (sprintf "%s MN = %s MPa" (f3 (shellM.SigmaZThermal * shellM.Area / 1e6)) (f1 (shellM.SigmaZThermal / 1e6)))
        fea "CARICO NETTO PER TUBO (LC1 esercizio)" (sprintf "%s kN in %s" (f2 (abs fTb / nTb / 1000.0)) (if fTb > 0.0 then "TRAZIONE (sfilamento)" else "compressione"))
        fea "CARICO PER TUBO in LC2 (caldo non in press.)" (sprintf "%s kN in %s" (f2 (abs fTbT / nTb / 1000.0)) (if fTbT < 0.0 then "COMPRESSIONE (buckling)" else "trazione"))
        fea "Allungamento comune imposto dalle piastre" (sprintf "%s mm" (f2 (stF.CommonDelta * 1000.0)))

        feaHdr "6. CONDIZIONI DI CARICO DA ANALIZZARE"
        fea "LC1 - esercizio" "pressione + temperature di regime: i due effetti in parte si elidono sui tubi"
        fea "LC2 - caldo NON in pressione" "solo dilatazione impedita: caso severo per l'instabilita' dei tubi"
        fea "LC3 - prova idraulica a freddo" "solo pressione di prova, temperature uniformi"
        fea "LC4 - avviamento / transitorio" "gradiente fra piastra lato gas e piastra lato acqua"
        fea "LC5 - condizione PULITA" "flusso di picco maggiore, temperature metalliche diverse (sez. 5c)"

        feaHdr "7. CHE COSA VERIFICARE"
        fea "Piastra tubiera" "flessione, tensioni sul legamento fra fori, effetto del foro centrale del by-pass"
        fea "Giunto tubo-piastra" "sfilamento secondo ASME VIII-1 UW-20; tenuta della mandrinatura o della saldatura"
        fea "Zona di transizione piastra-virola" "e' dove si concentra la discontinuita' geometrica e termica"
        fea "Tubi in prossimita' dell'incastro" "flessione indotta dalla rotazione della piastra, non catturata dallo screening"
        fea "Fatica" "cicli di avviamento e fermata, con i gradienti della LC4"
        fea "Classificazione delle tensioni" "Pm / Pm+Pb / Pm+Pb+Q secondo ASME VIII Div.2 Parte 5"
        sb.AppendLine() |> ignore
        para sb "  " "NOTA SULLA CLASSIFICAZIONE. Le tensioni di pressione sono PRIMARIE: il loro superamento porta al collasso e i limiti sono stringenti. Quelle da dilatazione impedita e da gradiente termico sono SECONDARIE: si rilassano e i limiti sono piu' larghi, ma governano la fatica. Nella modellazione FEA vanno tenute separate, perche' sommarle e confrontarle con un unico limite porta a sovradimensionare la piastra senza motivo, oppure a sottovalutare la fatica."
        sb.AppendLine() |> ignore

        hdr sb "9. Diagnostica di progetto"
        if r.Warnings.IsEmpty then sb.AppendLine("  Nessuna anomalia rilevata dai criteri implementati.") |> ignore
        else r.Warnings |> List.iteri (fun i x -> sb.AppendLine(sprintf "  [%d] %s" (i + 1) x).AppendLine() |> ignore)

        hdr sb "10. Profilo assiale (media sulle bande)"
        sb.AppendLine("   z[m]  Tgas[C]  spread[K]  q''med  q''max  Tmi max  Tmo max  x_top  alpha  DNBR  w_field  w_byp  Vmix") |> ignore
        sb.AppendLine("                             [kW/m2] [kW/m2]   [C]      [C]                        [kg/sm] [kg/sm] [m/s]") |> ignore
        sb.AppendLine(line) |> ignore
        let step = max 1 (List.length r.Axial / 30)
        r.Axial |> List.iteri (fun i a ->
            if i % step = 0 || i = List.length r.Axial - 1 then
                sb.AppendLine(
                    sprintf "%7s %8s %10s %7s %7s %8s %8s %6s %6s %5s %8s %7s %6s"
                        (f2 a.Z) (f1 (kToC a.TGasMean)) (f1 (a.TGasMax - a.TGasMin))
                        (f0 (a.QFluxMean / 1000.0)) (f0 (a.QFluxMax / 1000.0))
                        (f0 (kToC a.TMetalInMax)) (f0 (kToC a.TMetalOutMax))
                        (f4 a.XTop) (f3 a.AlphaTop) (f2 a.DNBRMin)
                        (f1 a.WFieldLin) (f1 a.WBypassLin) (f2 a.VelMixOut)) |> ignore)
        sb.AppendLine(line) |> ignore
        legend sb
            [ "z [m]", "Ascissa lungo l'asse dell'apparecchio, misurata dalla faccia interna della piastra tubiera d'ingresso gas. La maglia e' graduata: celle di ~20 mm all'imbocco, piu' larghe verso l'uscita."
              "Tgas [C]", "Temperatura del gas mediata sui tubi (pesata sul numero di tubi di ogni banda)."
              "spread [K]", "Differenza fra la temperatura del gas nella banda piu' calda e in quella piu' fredda, alla stessa ascissa: e' la misura diretta dell'effetto fascio sul lato gas."
              "q'' med [kW/m2]", "Flusso termico medio sulla sezione, riferito alla superficie esterna."
              "q'' max [kW/m2]", "Flusso termico della cella peggiore della sezione."
              "Tmi max [C]", "Massima temperatura del metallo sulla superficie interna nella sezione."
              "Tmo max [C]", "Massima temperatura del metallo sulla superficie esterna nella sezione."
              "x_top", "Titolo massico della miscela che esce dalla banda superiore, cioe' quella che va ai riser."
              "alpha", "Frazione di vuoto corrispondente a x_top."
              "DNBR", "Minimo margine su DNB fra tutte le celle della sezione."
              "w_field [kg/sm]", "Portata d'acqua che attraversa il fascio per METRO di lunghezza dell'apparecchio. Segue la produzione locale di vapore, quindi decade lungo z come il flusso termico."
              "w_byp [kg/sm]", "Portata nei canali liberi non intubati, sempre per metro. Positiva = sale, negativa = scende (ricircolo interno). Nulla se i diaframmi chiudono la corona periferica."
              "Vmix [m/s]", "Velocita' della miscela in uscita dal fascio, con densita' omogenea." ]

        hdr sb "11. Note di lettura (definizioni)"
        let g (t: string) (body: string) =
            sb.AppendLine(sprintf "  %s" t) |> ignore
            for ln in body.Split('\n') do sb.AppendLine(sprintf "    %s" (ln.Trim())) |> ignore
            sb.AppendLine() |> ignore

        g "UNITA' DELLE TEMPERATURE: K oppure °C?"
            "Una DIFFERENZA di temperatura ha lo stesso valore numerico in kelvin e in gradi
             Celsius, perche' le due scale hanno la stessa ampiezza di grado e differiscono solo
             per l'origine (0 K = -273.15 °C). Quindi 13.1 K di surriscaldamento = 13.1 °C di
             surriscaldamento: e' la stessa cosa. Per convenzione tecnica le DIFFERENZE si
             scrivono in K e i LIVELLI in °C, proprio per non lasciare dubbi. In questo report:
             tutti i valori etichettati [K] sono differenze, tutti quelli [°C] sono livelli."

        g "SURRISCALDAMENTO DI PARETE (wall superheat)"
            "E' la differenza fra la temperatura della superficie a contatto con l'acqua bollente
             e la temperatura di saturazione:  dT_sup = T_parete - Tsat = q\" / h_ebollizione.
             E' la variabile che governa l'ebollizione nucleata:
               - sotto ~1 K non ci sono ancora bolle (serve superare il dT di ONB, onset of
                 nucleate boiling: le cavita' della superficie devono attivarsi);
               - fra qualche K e il dT critico si e' in ebollizione nucleata pienamente
                 sviluppata: e' la zona di lavoro, h molto alto e stabile;
               - oltre il dT critico (il ginocchio della curva di Nukiyama) le bolle si
                 fondono in un film continuo di vapore: h crolla di 1-2 ordini di grandezza e
                 la parete si scalda di centinaia di gradi in pochi minuti.
             ATTENZIONE: il surriscaldamento di parete NON e' la differenza fra metallo e Tsat.
             Fra il metallo e l'acqua c'e' il deposito (magnetite, ossidi): il report riporta
             separatamente il salto attraverso il deposito, che nei WHB reali e' spesso il
             contributo dominante alla temperatura del metallo."

        g "DNBR LOCALE (Departure from Nucleate Boiling Ratio)"
            "DNBR = q\"_critico,locale / q\"_effettivo,locale  [adimensionale]
             E' il rapporto fra il flusso termico che farebbe passare l'ebollizione da nucleata
             a film (il CHF, Critical Heat Flux) e il flusso termico che il tubo sta realmente
             scambiando in quel punto. E' un MARGINE:
               DNBR = 3   -> si sta lavorando a un terzo del flusso che innescherebbe il DNB
               DNBR = 1   -> si e' esattamente al limite
               DNBR < 1   -> il criterio e' violato in quel punto
             Perche' 'locale': il CHF non e' un numero unico dell'apparecchio. Dipende dalla
             pressione (via Mostinski/Zuber), dalla geometria del fascio (fattore phi_b di
             Palen) e soprattutto dal TITOLO locale: piu' vapore c'e' gia' nella miscela che
             lava il tubo, meno flusso termico serve per scoprire la parete. Per questo il DNBR
             minimo di questo apparecchio non cade dove il flusso termico e' massimo, ma nella
             banda superiore del fascio, dove il titolo e' massimo perche' l'acqua ha gia'
             attraversato tutte le bande sottostanti.
             Il CHF di fascio usato qui e' quello di Palen (phi_b), tarato su ribollitori kettle:
             e' un criterio conservativo per un fascio attraversato da crossflow forzato. Va
             letto insieme al criterio pratico sul flusso termico massimo (250-350 kW/m2)."

        g "DNB - CRISI DI EBOLLIZIONE (il fenomeno)"
            "DNB sta per Departure from Nucleate Boiling: e' il FENOMENO, mentre DNBR e' il numero
             che ne misura il margine.
             In ebollizione nucleata le bolle nascono su singoli siti della parete, si staccano e
             l'acqua le rimpiazza immediatamente. E' il regime piu' efficiente che esista: il
             coefficiente di scambio e' altissimo e il metallo resta a pochi gradi sopra la
             temperatura dell'acqua, anche con flussi termici enormi.
             Se pero' il flusso cresce oltre un valore critico, le bolle si generano piu' in
             fretta di quanto l'acqua riesca a rimpiazzarle: si toccano, si saldano fra loro e
             formano un FILM DI VAPORE continuo che avvolge la parete. Il vapore conduce circa
             venti volte peggio dell'acqua, quindi il coefficiente di scambio crolla di colpo.
             Il calore continua ad arrivare dal gas ma non riesce piu' a passare, e la
             temperatura del metallo sale di CENTINAIA di gradi in pochi secondi.
             Nei fasci tubieri il fenomeno prende anche il nome di STEAM BLANKETING, perche' il
             film si forma preferenzialmente sui ranghi alti, dove la miscela che arriva e' gia'
             carica del vapore prodotto sotto.
             LA COSA IMPORTANTE DA CAPIRE e' che NON e' un peggioramento progressivo: e' un
             salto. Sotto la soglia non succede nulla, sopra la soglia il tubo si scopre e cede
             per sfondamento a caldo in tempi brevissimi. Per questo non si progetta al limite
             ma con un margine, ed e' quello che il DNBR misura."

        g "RAPPORTO DI CIRCOLAZIONE (CR)"
            "CR = portata d'acqua circolante / portata di vapore prodotta   [adimensionale]
             Equivalentemente il titolo in uscita dal fascio e' x = 1/CR: CR = 10 significa che
             di ogni 10 kg di miscela che escono dal mantello 1 kg e' vapore e 9 kg sono acqua
             che torna al corpo cilindrico. La regola pratica e' CR >= 10, cioe' x <= 0.10.
             COME SI CALCOLA. Non e' un dato, e' il risultato di un bilancio idraulico. In un
             circuito a termosifone la forza motrice e' la differenza di peso fra la colonna
             di acqua satura che scende (downcomer) e la colonna di miscela bifase, piu'
             leggera, che sale (fascio + riser):
                 dp_motore = g * [ rho_liq * H_dc - rho_fascio * H_fascio - rho_riser * H_riser ]
             dove H_dc e' l'altezza dal livello acqua nel drum al fondo del fascio, H_fascio
             l'altezza attraversata nel fascio e H_riser il tratto dal cielo del mantello al
             livello nel drum. Le densita' delle colonne bifase dipendono dalla frazione di
             vuoto, quindi dal titolo, quindi da CR: e' un problema implicito.
             A questa forza motrice si oppongono le perdite di carico del giro:
                 dp_perdite(CR) = downcomer + bocchello ingresso + attraversamento fascio
                                  + bocchello uscita + riser + interne del corpo cilindrico
             che crescono con il quadrato della portata, cioe' con CR^2.
             Il CR di esercizio e' il valore che annulla dp_motore(CR) - dp_perdite(CR); il
             codice lo trova per bisezione. Aumentare CR si ottiene abbassando le perdite
             (riser e downcomer piu' grandi) o alzando il dislivello asse drum - asse WHB.
             Il CR EFFICACE riportato a parte tiene conto anche del ricircolo interno al
             mantello attraverso la corona anulare lasciata aperta dai diaframmi."

        g "TITOLO E FRAZIONE DI VUOTO"
            "Titolo x = portata di vapore / portata totale (rapporto di MASSE).
             Frazione di vuoto alpha = area occupata dal vapore / area totale (rapporto di
             AREE). A 118 bar il vapore e' 10 volte meno denso del liquido, quindi un titolo
             modesto (x = 0.10) corrisponde gia' a una frazione di vuoto elevata (alpha = 0.45):
             meta' della sezione e' vapore. E' alpha, non x, a dire se i ranghi alti del fascio
             restano bagnati."

        g "CAPPELLO / DEFLETTORE ANTITRASCINAMENTO SUL BOCCHELLO RISER"
            "E' una piccola lamiera piegata a cappello, saldata dentro il mantello sopra la bocca
             del bocchello di uscita, con l'apertura rivolta di lato o verso il basso. Serve a due
             cose. (1) Impedisce che il bocchello 'aspiri' direttamente dal pelo della miscela il
             getto piu' ricco di vapore che sale dal fascio sottostante: senza cappello si crea un
             cammino preferenziale e quel tratto di fascio viene lavato meno degli altri. (2) Rompe
             il vortice che tenderebbe a formarsi sopra la bocca, che altrimenti trascinerebbe
             vapore in modo intermittente facendo pulsare il riser. In pratica costringe la miscela
             a fare una piccola deviazione prima di entrare nel bocchello, uniformando il prelievo
             lungo il mantello. L'equivalente sul lato discesa e' il rompivortice (vortex breaker)
             sullo stacco del downcomer dal corpo cilindrico, che ha lo stesso scopo ma per evitare
             che il liquido si porti dietro vapore verso il basso (carry-under)."

        g "MOTO A TAPPI (SLUG) NEI RISER"
            "In un tubo verticale con miscela acqua-vapore le bolle possono coalescere in
             grandi bolle di Taylor che occupano quasi tutta la sezione, separate da tappi di
             liquido. Il risultato e' un flusso intermittente: la portata e la pressione
             pulsano a 0.5-5 Hz e la forzante si scarica sui supporti, sui bocchelli e sulla
             piastra tubiera. Si evita uscendo dal campo slug della mappa di Taitel-Dukler:
             o verso il basso (regime a bolle, titoli bassi e diametri piccoli) o verso l'alto
             (churn/anulare, alte velocita'). Per i riser di una caldaia la strada praticabile
             e' la seconda: riser piu' numerosi e piu' piccoli, che alzano la velocita'."
        sb.AppendLine("  Nota: tutte le portate del caso sono maggiorate del 10% come da datasheet.") |> ignore
        sb.ToString()


    /// <summary>
    /// Calculates or returns synthesis for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let synthesis (r: DesignResult) =
        let c = r.Case
        let sb = StringBuilder()
        let sevTag = function Critical -> "[CRITICO]  " | Warning -> "[ATTENZIONE]" | Note -> "[NOTA]     "
        let rank = function Critical -> 0 | Warning -> 1 | Note -> 2
        sb.AppendLine(dline) |> ignore
        sb.AppendLine("WHB / PGC - SINTESI DELLE CRITICITA'") |> ignore
        sb.AppendLine(sprintf "Caso: %s" c.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine(dline) |> ignore
        sb.AppendLine() |> ignore

        let nC = r.Findings |> List.filter (fun f -> f.Severity = Critical) |> List.length
        let nW = r.Findings |> List.filter (fun f -> f.Severity = Warning) |> List.length
        let nN = r.Findings |> List.filter (fun f -> f.Severity = Note) |> List.length
        sb.AppendLine(sprintf "  ESITO:  %d criticita' | %d attenzioni | %d note" nC nW nN) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  NUMERI CHIAVE") |> ignore
        let hot = r.Cells |> List.filter (fun x -> not x.InFerrule)
        let kv2 k v = sb.AppendLine(sprintf "    %-42s %s" k v) |> ignore
        kv2 "Potenza / vapore" (sprintf "%s MW / %s t/h" (f2 (r.Duty / 1e6)) (f0 (r.SteamProduction * 3.6)))
        kv2 "T gas uscita (media)" (sprintf "%s °C" (f1 (kToC r.TGasOutMean)))
        kv2 "Flusso termico di picco" (sprintf "%s kW/m2" (f0 ((hot |> List.map (fun x -> x.QFluxOut) |> List.max) / 1000.0)))
        kv2 "T metallo massima (interna)" (sprintf "%s °C su limite %s °C" (f0 (kToC (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max))) (f0 c.Material.TmaxDesign))
        kv2 "DNBR locale minimo (INDICATORE, vedi 5d)"
            (sprintf "%s  - il criterio di riferimento e' 2.0, ma nessun modello di CHF disponibile e' tarato su questa geometria: il valore va usato per confronti relativi, non in assoluto"
                 (f2 (hot |> List.map (fun x -> x.DNBR) |> List.min)))
        kv2 "Rapporto di circolazione" (sprintf "%s  su minimo 10.0" (f1 r.Circulation.CirculationRatio))
        kv2 "Frazione di vuoto massima" (sprintf "%s  su limite 0.70" (f3 (r.Cells |> List.map (fun x -> x.Alpha) |> List.max)))
        kv2 "Dilatazione differenziale tubo-mantello"
            (sprintf "%s mm -> %s MPa compr. nei tubi (utilizzo %s%% a instabilita')"
                 (f2 (r.FixedTubesheet.DeltaFree * 1000.0)) (f1 (r.FixedTubesheet.SigmaTube / 1e6))
                 (f0 (100.0 * r.FixedTubesheet.BucklingUtilisation)))
        kv2 "Carico per tubo sulla giunzione tubo-piastra" (sprintf "%s kN" (f1 (r.FixedTubesheet.ForcePerTube / 1000.0)))
        kv2 "dP lato gas" (sprintf "%s mbar su ammesso 300 mbar" (f0 (r.DpGas / 100.0)))
        if c.Ferrule.Enabled then
            let ferruleLength =
                BundleSolver.ferruleClasses c.Ferrule
                |> List.sumBy (fun (fr, l) -> fr * l)
            let compIn = GasProps.normalize c.Gas.Composition
            let propsIn = GasProps.mixReal c.Gas.MixingRule c.Gas.RealGas compIn c.Gas.TIn c.Gas.PIn c.Gas.Z
            let dpFerrule =
                BundleSolver.ferrulePressureDropEstimate
                    c.Ferrule c.Tube.Di c.Tube.Roughness (c.Gas.MassFlow / float c.Tube.NTubes) propsIn ferruleLength
            let dpShare = 100.0 * dpFerrule / max 1.0 r.DpGas
            let paperThk = BundleSolver.ferruleInsulationThickness c.Ferrule c.Tube.Di
            kv2 "Ferrula: dP stimata / quota dP gas"
                (sprintf "%s mbar per tubo / %s%%" (f2 (dpFerrule / 100.0)) (f1 dpShare))
            kv2 "Ferrula: carta isolante radiale"
                (sprintf "%s mm - %s" (f2 (paperThk * 1000.0)) (BundleSolver.ferruleInsulationFitStatus c.Ferrule c.Tube.Di))
        kv2 "dP circuito acqua/vapore"
            (sprintf "DC %s | riser %s | fascio %s | drum/calm box %s mbar"
                 (f0 (r.Circulation.DpDowncomer / 100.0))
                 (f0 (r.Circulation.DpRiser / 100.0))
                 (f0 (r.Circulation.DpBundle / 100.0))
                 (f0 (r.Circulation.DpNozzles / 100.0)))
        let whbWater =
            max 0.0
                (Math.PI / 4.0 * c.Tube.ShellId * c.Tube.ShellId * c.Tube.Length
                 - Math.PI / 4.0 * c.Tube.Do * c.Tube.Do * c.Tube.Length * float c.Tube.NTubes
                 - (if c.Bypass.Enabled then Math.PI / 4.0 * c.Bypass.PipeOd * c.Bypass.PipeOd * c.Tube.Length else 0.0))
        let riserWater = c.Loop.Risers |> List.filter (fun l -> l.Connected) |> List.sumBy lineWaterVolume
        let downcomerWater = c.Loop.Downcomers |> List.filter (fun l -> l.Connected) |> List.sumBy lineWaterVolume
        let drumWater = if c.Loop.Drum.Enabled then drumWaterVolume c.Loop.Drum else 0.0
        let rhoFerrule = densityOf c.FerruleMaterial
        let ferruleMetal =
            if c.Ferrule.Enabled then
                let length = c.Ferrule.Lengths |> List.sumBy (fun (frac, len) -> frac * len)
                Math.PI / 4.0 * max 0.0 (c.Ferrule.SleeveOd ** 2.0 - c.Ferrule.Bore ** 2.0) * length * float c.Tube.NTubes * rhoFerrule
            else 0.0
        let tubeMetal = Math.PI / 4.0 * (c.Tube.Do ** 2.0 - c.Tube.Di ** 2.0) * c.Tube.Length * float c.Tube.NTubes * densityOf c.Material
        let shellMetal =
            Math.PI / 4.0 * ((c.Tube.ShellId + 2.0 * c.ShellThickness) ** 2.0 - c.Tube.ShellId ** 2.0) * c.Tube.Length * densityOf c.ShellMaterial
        let baffleMetal =
            let count = max 0 (List.length c.BaffleSpans - 1)
            let gross = Math.PI / 4.0 * c.Tube.BaffleOd * c.Tube.BaffleOd
            let holes = Math.PI / 4.0 * c.Tube.Do * c.Tube.Do * float c.Tube.NTubes
            max 0.0 (gross - holes) * c.BaffleThickness * float count * densityOf c.ShellMaterial
        let riserMetal = c.Loop.Risers |> List.filter (fun l -> l.Connected) |> List.sumBy (lineMetalWeight (densityOf c.ShellMaterial))
        let downcomerMetal = c.Loop.Downcomers |> List.filter (fun l -> l.Connected) |> List.sumBy (lineMetalWeight (densityOf c.ShellMaterial))
        let drumMetal =
            if c.Loop.Drum.Enabled then
                let d = c.Loop.Drum
                Math.PI / 4.0 * ((d.ShellId + 2.0 * c.ShellThickness) ** 2.0 - d.ShellId ** 2.0) * d.Length * densityOf c.ShellMaterial
            else 0.0
        let bypassMetal =
            if c.Bypass.Enabled then
                let liner = Math.PI / 4.0 * (c.Bypass.LinerOd ** 2.0 - c.Bypass.LinerId ** 2.0) * c.Tube.Length * densityOf c.Bypass.LinerMaterial
                let pipe = Math.PI / 4.0 * (c.Bypass.PipeOd ** 2.0 - c.Bypass.InsulOd ** 2.0) * c.Tube.Length * densityOf c.Bypass.PipeMaterial
                liner + pipe
            else 0.0
        kv2 "Inventory acqua / peso metallo"
            (sprintf "%s m3 / %s t"
                 (f1 (whbWater + riserWater + downcomerWater + drumWater))
                 (f1 ((tubeMetal + shellMetal + baffleMetal + ferruleMetal + riserMetal + downcomerMetal + drumMetal + bypassMetal) / 1000.0)))
        (let vw = r.Vibration |> List.maxBy (fun v -> v.FeiRatio)
         kv2 "VIBRAZIONI - V/Vcrit (istab. fluido-elastica)"
             (sprintf "%s  su limite 0.8   [reticolo %s, K = %s]"
                  (f2 vw.FeiRatio) (Vibration.layoutName c.TubeLayout) (f1 vw.KConnors))
         kv2 "  campata massima ammessa / campata assunta"
             (sprintf "%s m  contro  %s m assunti" (f2 (Vibration.maxSpan 0.8 vw)) (f2 vw.Span)))
        let ws = r.Stress.Cells |> List.maxBy (fun x -> x.Utilisation)
        kv2 "Tensione equivalente massima (Lame' + assiale)"
            (sprintf "%s MPa = %s%% di Sy  (%s, z = %s m%s)"
                 (f0 (ws.SigmaVMMax / 1e6)) (f0 (100.0 * ws.Utilisation)) ws.Component (f2 ws.Z)
                 (if ws.J >= 0 then sprintf ", banda %d" ws.J else ""))
        kv2 "Carico di estremita' da pressione (trazione)"
            (sprintf "%s MN, che compensa la compressione termica" (f2 (r.Stress.PressureEndLoad / 1e6)))
        (match r.Stress.Bucklings |> List.filter (fun b -> b.CollapseUtil > 0.0) with
         | [] -> ()
         | bs ->
            let w = bs |> List.maxBy (fun b -> b.CollapseUtil)
            kv2 "Pressione esterna: caso peggiore"
                (sprintf "%s: %s bar su %s bar di collasso (utilizzo %s%%)"
                     (w.Label.Split(':').[0]) (f1 (w.PExtNet / 1e5)) (f0 (w.PCollapse / 1e5)) (f0 (100.0 * w.CollapseUtil))))
        (match r.Valve with
         | Some v ->
            kv2 "Farfalla del by-pass in esercizio normale"
                (sprintf "%s° di apertura (finestra ammessa %s° - %s°)"
                     (f1 v.Normal.OpenDeg) (f1 v.MinOpen.OpenDeg) (f1 v.MaxOpen.OpenDeg))
            kv2 "By-pass: frazione / dP libero / dP valvola"
                (sprintf "%s%% / %s mbar / %s mbar"
                     (f2 (100.0 * v.Normal.Fraction))
                     (f1 ((v.Normal.DpBypassTot - v.Normal.DpValve) / 100.0))
                     (f1 (v.Normal.DpValve / 100.0)))
            kv2 "  sensibilita' della regolazione"
                (sprintf "%s K di T miscelata per grado di stelo"
                     (f2 (abs (v.MaxOpen.TMixed - v.MinOpen.TMixed) / max 1.0 (v.MaxOpen.OpenDeg - v.MinOpen.OpenDeg))))
         | None -> ())
        let validityWarnings =
            r.Findings
            |> List.filter (fun f -> f.Area.Contains("VALIDITA"))
            |> List.length
        if validityWarnings > 0 then
            kv2 "Warning validita' correlazioni/proprieta'" (sprintf "%d da verificare" validityWarnings)
        (match r.LineChecks |> List.filter (fun l -> not l.Connected) with
         | [] -> ()
         | nc -> kv2 "BOCCHELLI NON COLLEGATI" (nc |> List.map (fun l -> l.Tag) |> String.concat ", "))
        sb.AppendLine() |> ignore

        definizioni sb

        if r.Findings.IsEmpty then
            sb.AppendLine("  Nessuna criticita' rilevata dai criteri implementati.") |> ignore
        else
            for f in r.Findings |> List.sortBy (fun f -> (rank f.Severity, f.Area)) do
                sb.AppendLine(dline) |> ignore
                sb.AppendLine(sprintf "%s %s / %s" (sevTag f.Severity) f.Area f.Title) |> ignore
                sb.AppendLine(String('-', 96)) |> ignore
                sb.AppendLine(sprintf "  valore .... %s" f.Value) |> ignore
                sb.AppendLine(sprintf "  criterio .. %s" f.Limit) |> ignore
                sb.AppendLine(sprintf "  DOVE ...... %s" f.Where) |> ignore
                if f.Detail <> "" then
                    sb.AppendLine("  perche' ...") |> ignore
                    para sb "              " f.Detail
                if f.Action <> "" then
                    sb.AppendLine("  AZIONE ....") |> ignore
                    para sb "              " f.Action
                sb.AppendLine() |> ignore
            sb.AppendLine(dline) |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine("  MAPPA DELLE ZONE CRITICHE (dove guardare sull'apparecchio)") |> ignore
        sb.AppendLine(String('-', 96)) |> ignore
        let qm = hot |> List.maxBy (fun x -> x.QFluxOut)
        let dn = hot |> List.minBy (fun x -> x.DNBR)
        let al = r.Cells |> List.maxBy (fun x -> x.Alpha)
        para sb "  " (sprintf "1) IMBOCCO GAS, subito a valle della ferrula (z = %.2f m su %.2f m totali, cioe' i primi %.0f cm): e' dove cade il picco di flusso termico (%.0f kW/m2) e la temperatura metallica massima. Ispezione boroscopica dei primi 500 mm di tubo e verifica dell'integrita' delle ferrule." qm.Z c.Tube.Length (qm.Z * 100.0) (qm.QFluxOut / 1000.0))
        para sb "  " (sprintf "2) BANDA SUPERIORE DEL FASCIO (y = %+.2f m rispetto all'asse, cioe' i ranghi piu' alti): e' dove il titolo e la frazione di vuoto sono massimi (x = %.3f, alpha = %.2f) e dove cade il DNBR minimo (%.2f a z = %.2f m). E' la zona esposta allo steam blanketing." al.Y al.XOut al.Alpha dn.DNBR dn.Z)
        para sb "  " (sprintf "3) GIUNZIONE TUBO-PIASTRA: carico assiale di %.1f kN per tubo dalla dilatazione impedita, piu' i termini di pressione non inclusi in questo screening. Da verificare con TEMA RCB-7.16 / ASME UHX-13." (r.FixedTubesheet.ForcePerTube / 1000.0))
        para sb "  " (sprintf "4) CIRCUITO DI CIRCOLAZIONE: CR = %.1f contro il minimo di 10. Le perdite si ripartiscono in %.0f mbar sui riser, %.0f sui downcomer, %.0f sul fascio e %.0f sulle interne del corpo cilindrico (quest'ultimo dato e' un'assunzione da confermare col costruttore)." r.Circulation.CirculationRatio (r.Circulation.DpRiser / 100.0) (r.Circulation.DpDowncomer / 100.0) (r.Circulation.DpBundle / 100.0) (r.Circulation.DpNozzles / 100.0))
        sb.AppendLine(String('-', 96)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  Il dettaglio completo, con le spiegazioni di ogni grandezza, e' nel report esteso.") |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns csvcells for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let csvCells (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("i;j;z_m;y_m;n_tubi;T_gas_C;p_gas_bar;v_gas_ms;Re_gas;h_conv_Wm2K;h_rad_Wm2K;eps_gas;x_in;x_out;alpha;G_cross_kgm2s;v_cross_ms;h_boil_Wm2K;U_o_Wm2K;q_lin_Wm;q_int_kWm2;q_est_kWm2;T_met_int_C;T_met_mid_C;T_met_est_C;T_met_media_spessore_C;T_parete_bollente_C;dT_superheat_K;dT_deposito_K;dT_met_sat_K;q_CHF_loc_kWm2;DNBR;ferrula") |> ignore
        for x in r.Cells do
            sb.AppendLine(String.Join(";",
                [ string x.I; string x.J; f3 x.Z; f4 x.Y; f1 x.NTubes
                  f2 (kToC x.TGas); f4 (paToBar x.PGas); f3 x.VelGas; f0 x.ReGas
                  f1 x.HConvGas; f2 x.HRadGas; f4 x.EpsGas
                  f5 x.XIn; f5 x.XOut; f4 x.Alpha; f2 x.GCross; f3 x.VelCross
                  f1 x.HBoil; f1 x.U_o; f1 x.QLin; f2 (x.QFluxIn / 1000.0); f2 (x.QFluxOut / 1000.0)
                  f2 (kToC x.TMetalIn); f2 (kToC x.TMetalMid); f2 (kToC x.TMetalOut); f2 (kToC x.TMetalWallAvg)
                  f2 (kToC x.TWallBoil); f3 x.DTsatWall; f2 x.DTDeposit; f2 x.DTMetalSat
                  f0 (x.QCritLocal / 1000.0); f2 x.DNBR; (if x.InFerrule then "1" else "0") ])) |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns extract for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private extract (r: DesignResult) (titolo: string) (marker: string) (fine: string) =
        let full = text r
        let i = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase)
        let j = full.IndexOf(fine, StringComparison.OrdinalIgnoreCase)
        let body =
            if i < 0 then "(sezione non disponibile)"
            elif j > i then full.Substring(i, j - i)
            else full.Substring(i)
        let sb = StringBuilder()
        sb.AppendLine(dline) |> ignore
        sb.AppendLine(titolo) |> ignore
        sb.AppendLine(sprintf "Caso: %s" r.Case.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine("Estratto dal report esteso: stessi dati, stesso calcolo, nessuna rielaborazione.") |> ignore
        sb.AppendLine(dline) |> ignore
        definizioni sb
        sb.Append(body) |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns maldistributiontext for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let maldistributionText (r: DesignResult) =
        extract r "WHB / PGC - MALDISTRIBUZIONE DELLA PORTATA DI GAS FRA I TUBI"
            "6F. MALDISTRIBUZIONE" "6E. TRANSITORI"

    /// <summary>
    /// Calculates or returns vibrationtext for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let vibrationText (r: DesignResult) =
        extract r "WHB / PGC - VIBRAZIONI INDOTTE DAL FLUSSO (FIV)"
            "6D. VIBRAZIONI" "6F. MALDISTRIBUZIONE"

    /// <summary>
    /// Calculates or returns csvstress for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let csvStress (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("componente;i;j;classe;z_m;y_m;T_met_int_C;T_met_est_C;T_met_media_C;dT_spessore_K;p_int_bar;p_est_bar;sZ_membr_MPa;sZ_termico_MPa;sZ_pressione_MPa;punto;r_mm;sigma_R_MPa;sigma_theta_MPa;sigma_Z_MPa;sigma_VM_MPa;sigma_Tresca_MPa;Sy_MPa;utilizzo_pc") |> ignore
        for c in r.Stress.Cells do
            for p in c.Points do
                sb.AppendLine(String.Join(";",
                    [ c.Component; string c.I; string c.J; string c.C; f3 c.Z; f4 c.Y
                      f2 (kToC c.TMetalIn); f2 (kToC c.TMetalOut); f2 (kToC c.TMetalAvg); f2 c.DTWall
                      f3 (paToBar c.PInt); f3 (paToBar c.PExt)
                      f3 (c.SigmaZMembrane / 1e6); f3 (c.SigmaZThermal / 1e6); f3 (c.SigmaZPressure / 1e6)
                      p.Position; f2 (p.R * 1000.0)
                      f3 (p.SigmaR / 1e6); f3 (p.SigmaTheta / 1e6); f3 (p.SigmaZ / 1e6)
                      f3 (p.SigmaVM / 1e6); f3 (p.SigmaTresca / 1e6)
                      f1 (c.Sy / 1e6); f2 (100.0 * p.SigmaVM / c.Sy) ])) |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns csvvalve for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let csvValve (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("apertura_gradi;chiusura_gradi;zeta;frazione_bypass_pc;w_bypass_kgs;rho_kgm3;v_liner_ms;v_vena_ms;Mach;rhov2_vena_Pa;dp_valvola_mbar;dp_bypass_tot_mbar;dp_fascio_mbar;T_out_tubi_C;T_out_bypass_C;T_miscelata_C;potenza_MW;vapore_th;T_liner_max_C;nota") |> ignore
        match r.Valve with
        | None -> ()
        | Some v ->
            for p in v.Sweep do
                let note =
                    if abs (p.OpenDeg - v.Normal.OpenDeg) < 0.05 then "NORMALE"
                    elif abs (p.OpenDeg - v.MinOpen.OpenDeg) < 0.05 then "MINIMO"
                    elif abs (p.OpenDeg - v.MaxOpen.OpenDeg) < 0.05 then "MASSIMO"
                    else ""
                sb.AppendLine(String.Join(";",
                    [ f2 p.OpenDeg; f2 p.ClosureDeg; f3 p.Zeta; f4 (100.0 * p.Fraction)
                      f4 p.MassFlowBypass; f3 p.RhoValve; f3 p.VelPipe; f2 p.VelThroat; f4 p.Mach
                      f0 p.RhoV2Throat; f2 (p.DpValve / 100.0); f2 (p.DpBypassTot / 100.0)
                      f2 (p.DpTubes / 100.0)
                      f2 (kToC p.TOutTubes); f2 (kToC p.TOutBypass); f2 (kToC p.TMixed)
                      f3 (p.Duty / 1e6); f1 (p.Steam * 3.6); f1 (kToC p.TLinerMax); note ])) |> ignore
        sb.ToString()

    /// <summary>
    /// Calculates or returns csvaxial for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let csvAxial (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("z_m;T_gas_med_C;T_gas_min_C;T_gas_max_C;q_med_kWm2;q_max_kWm2;T_met_int_max_C;T_met_est_max_C;vapore_lin_kgsm;duty_lin_kWm;w_field_kgsm;w_bypass_kgsm;x_top;alpha_top;G_cross;v_liq_in_ms;v_mix_out_ms;v_vap_out_ms;v_ax_bottom_ms;v_ax_top_ms;DNBR_min;vapore_cum_kgh;duty_cum_MW") |> ignore
        for a in r.Axial do
            sb.AppendLine(String.Join(";",
                [ f3 a.Z; f2 (kToC a.TGasMean); f2 (kToC a.TGasMin); f2 (kToC a.TGasMax)
                  f2 (a.QFluxMean / 1000.0); f2 (a.QFluxMax / 1000.0)
                  f2 (kToC a.TMetalInMax); f2 (kToC a.TMetalOutMax)
                  f4 a.SteamLin; f2 (a.DutyLin / 1000.0); f2 a.WFieldLin; f2 a.WBypassLin
                  f5 a.XTop; f4 a.AlphaTop; f2 a.GCross
                  f4 a.VelLiqIn; f4 a.VelMixOut; f3 a.VelVapOut
                  f4 a.VelAxialBottom; f4 a.VelAxialTop; f2 a.DNBRMin
                  f1 (a.SteamCum * 3600.0); f3 (a.DutyCum / 1e6) ])) |> ignore
        sb.ToString()
