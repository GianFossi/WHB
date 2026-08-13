namespace Whb.Core

/// Materiali per tubi di caldaia a recupero: conducibilità termica in funzione
/// della temperatura e limiti metallurgici indicativi di progetto.
module Materials =

    /// Proprietà meccaniche e termiche semplificate di un materiale.
    type Material =
        { Name: string
          /// k(T[°C]) [W/(m·K)]
          K: float -> float
          /// Temperatura metallo massima di progetto consigliata [°C]
          TmaxDesign: float
          /// Coefficiente MEDIO di dilatazione fra 20 °C e T[°C], alpha(T) [1/°C]
          Alpha: float -> float
          /// Modulo di elasticità E(T[°C]) [Pa] - valori indicativi, da
          /// confermare su ASME II-D per il calcolo di codice
          E: float -> float
          /// Carico di snervamento Sy(T[°C]) [Pa] - indicativo
          Sy: float -> float
          /// Finestra di metal dusting [°C] (min, max) - None se non suscettibile
          MetalDusting: (float * float) option
          Note: string }

    let private lin (k0: float) (slope: float) = fun (t: float) -> k0 + slope * t

    /// SA-210 A1 / SA-192 - acciaio al carbonio per tubi di caldaia
    let carbonSteel =
        { Name = "SA-192 / SA-210 A1 (acciaio al carbonio)"
          K = lin 52.0 -0.028
          E = (fun t -> 2.07e+11 - 5.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.55e+08 - 220000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 450.0
          MetalDusting = Some(400.0, 800.0)
          Note = "Limite pratico 450 °C (creep + grafitizzazione oltre 425 °C)." }

    /// SA-209 T1 - 0.5Mo
    let t1Mo =
        { Name = "SA-209 T1 (0.5Mo)"
          K = lin 49.0 -0.024
          E = (fun t -> 2.08e+11 - 5.6e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.6e+08 - 210000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 480.0
          MetalDusting = Some(400.0, 800.0)
          Note = "Grafitizzazione possibile oltre 450 °C in esercizio prolungato." }

    /// SA-213 T11 - 1.25Cr-0.5Mo
    let t11 =
        { Name = "SA-213 T11 (1.25Cr-0.5Mo)"
          K = lin 42.0 -0.014
          E = (fun t -> 2.1e+11 - 5.8e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.75e+08 - 200000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.16e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 550.0
          MetalDusting = Some(430.0, 820.0)
          Note = "Buon compromesso per zone calde di WHB syngas." }

    /// SA-213 T22 - 2.25Cr-1Mo
    let t22 =
        { Name = "SA-213 T22 (2.25Cr-1Mo)"
          K = lin 38.0 -0.010
          E = (fun t -> 2.1e+11 - 5.7e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.75e+08 - 190000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.14e-05 + 3.2e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 580.0
          MetalDusting = Some(430.0, 850.0)
          Note = "Standard per tubi caldi di WHB reforming." }

    /// SA-213 TP321H - AISI 321H austenitico
    let ss321h =
        { Name = "SA-213 TP321H (austenitico)"
          K = fun t -> 14.5 + 0.0155 * t
          E = (fun t -> 1.95e+11 - 7e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.05e+08 - 120000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.6e-05 + 4.5e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 700.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Suscettibile a SCC da cloruri lato acqua: da evitare a contatto con BFW." }

    /// Alloy 800H / 800HT
    let alloy800 =
        { Name = "Alloy 800H/800HT"
          K = fun t -> 11.5 + 0.0165 * t
          E = (fun t -> 1.96e+11 - 6.2e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (1.7e+08 - 60000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.47e-05 + 4.3e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 800.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Tipico per ferrule/inserti e boccole in zona ingresso gas." }

    /// Alloy 601 / 602 CA - liner del by-pass interno, esposto al gas a 1000 °C
    let alloy601 =
        { Name = "Alloy 601 / 602 CA (liner by-pass)"
          K = fun t -> 11.3 + 0.0163 * t
          Alpha = (fun t -> 15.3e-6 + 4.0e-9 * (max 0.0 (t - 20.0)))
          E = (fun t -> 207e9 - 0.075e9 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (240e6 - 0.11e6 * (max 0.0 (t - 20.0))))
          TmaxDesign = 1100.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Alto Cr-Al, resistente a ossidazione e carburizzazione ad alta temperatura." }

    /// SA-516 Gr.70 - lamiera di mantello per servizio caldaia
    /// **SA-533 Gr.B Cl.2** - lamiera Mn-Mo-Ni bonificata, alta resistenza.
    /// E' il materiale del mantello, del corpo cilindrico e del tubo di
    /// contenimento del by-pass. Snervamento minimo 485 MPa a temperatura
    /// ambiente, molto superiore a un acciaio al carbonio da caldaia.
    let sa533b2 =
        { Name = "SA-533 Gr.B Cl.2 (Mn-Mo-Ni bonificato)"
          K = lin 41.0 -0.017
          E = (fun t -> 2.07e+11 - 5.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 200e6 (4.85e+08 - 380000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.18e-05 + 4.0e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 400.0
          MetalDusting = None
          Note = "Lamiera per recipienti a pressione, bonificata. Limite pratico ASME VIII ~371 °C." }

    /// **SB-168 UNS N06025** = Alloy 602 CA. Lega Ni-Cr-Fe con 2.2 % Al:
    /// e' l'alluminio a formare la pellicola protettiva che le da' la
    /// resistenza alla carburazione e al metal dusting, superiore a quella
    /// dell'Alloy 601.
    let alloy602 =
        { Name = "SB-168 UNS N06025 (Alloy 602 CA)"
          K = lin 10.5 0.0160
          E = (fun t -> 2.17e+11 - 7.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 60e6 (2.70e+08 - 105000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.42e-05 + 4.0e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 1200.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Lega per liner ad altissima temperatura: resistenza alla carburazione data dal 2.2 % di Al." }

    /// SA-516 Gr.70 - lamiera di mantello per servizio caldaia.
    let sa516 =
        { Name = "SA-516 Gr.70 (lamiera mantello)"
          K = lin 52.0 -0.028
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          E = (fun t -> 202e9 - 0.052e9 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (260e6 - 0.23e6 * (max 0.0 (t - 20.0))))
          TmaxDesign = 425.0
          MetalDusting = None
          Note = "Materiale di mantello tipico; non esposto al gas di processo." }

    /// Catalogo dei materiali disponibili per selezione testuale.
    let all = [ carbonSteel; t1Mo; t11; t22; ss321h; alloy800; alloy601; alloy602; sa516; sa533b2 ]

    /// Cerca un materiale per sottostringa del nome; se non trovato usa l'acciaio al carbonio.
    let byName (n: string) =
        all
        |> List.tryFind (fun m -> m.Name.ToLowerInvariant().Contains(n.ToLowerInvariant()))
        |> Option.defaultValue carbonSteel

    /// Dilatazione assiale [m] di un elemento di lunghezza l alla temperatura
    /// media equivalente t [°C], rispetto alla temperatura di montaggio.
    let elongation (m: Material) (tRoom: float) (l: float) (t: float) =
        m.Alpha t * (t - tRoom) * l

    /// Conducibilità di refrattari/isolanti per ferrule [W/(m·K)]
    module Refractory =
        /// Calcestruzzo refrattario leggero da ferrula
        let castableLight (t: float) = 0.35 + 0.00025 * t
        /// Fibra ceramica compattata
        let ceramicFibre (t: float) = 0.12 + 0.00035 * t
        /// Refrattario denso
        let castableDense (t: float) = 1.2 + 0.0003 * t
        /// Carta/feltro di allumina tipo Saffil compressa (isolante anulare ferrule)
        let saffilPaper (t: float) = 0.07 + 0.00015 * t
