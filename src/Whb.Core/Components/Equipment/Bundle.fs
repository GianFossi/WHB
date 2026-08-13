namespace Whb.Core

open System

/// Geometria del fascio tubiero suddiviso in **bande orizzontali**.
/// In una WHB a tubi da fumo orizzontale l'acqua attraversa il fascio dal
/// basso verso l'alto: le bande sono quindi percorse in serie e il titolo
/// cresce salendo. La banda più alta è quella esposta al rischio di
/// blanketing di vapore.
module Bundle =

    type Band =
        { Index: int
          /// Quota del centro banda rispetto all'asse del mantello [m] (positiva verso l'alto)
          Y: float
          /// Altezza della banda [m]
          Height: float
          /// Numero di tubi contenuti (valore reale, non intero)
          NTubes: float
          /// Larghezza totale del mantello a quella quota [m]
          ShellWidth: float
          /// Larghezza della zona intubata [m]
          TubedWidth: float
          /// Area libera al crossflow nella zona intubata, per metro assiale [m²/m]
          FieldFreeArea: float
          /// Area libera nei canali non intubati (corona periferica + anima centrale) [m²/m]
          BypassArea: float
          /// Ranghi di tubi attraversati nella banda
          Rows: float }

    /// Costruisce le bande.
    ///   shellId  : diametro interno mantello [m]
    ///   otl      : diametro esterno del fascio [m]
    ///   itl      : diametro dell'anima centrale non intubata [m] (0 se assente)
    ///   pitch    : passo [m]
    ///   dOut     : diametro esterno tubi [m]
    ///   nTubes   : numero di tubi totale (per normalizzazione)
    ///   nBands   : numero di bande
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
        // passo verticale per layout triangolare a 60° con file orizzontali
        let vPitch = pitch * 0.8660254
        let areaPerTube = pitch * pitch * 0.8660254

        let raw =
            [ for j in 0 .. n - 1 ->
                let y = -ro + h * (float j + 0.5)
                // integrazione della larghezza intubata sulla banda (5 sotto-punti)
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
                // l'anima centrale e' occupata dal tubo di by-pass: la sua
                // proiezione va sottratta dall'area libera verticale
                let blockedByPipe =
                    if bypassOd > 0.0 && abs y < bypassOd / 2.0 then
                        2.0 * sqrt (max 0.0 (bypassOd * bypassOd / 4.0 - y * y))
                    else 0.0
                max 1e-6 (sw - tw - blockedByPipe)
              Rows = h / vPitch })

    /// Area della sezione trasversale libera dei canali di bypass verticali
    /// (corona periferica + anima centrale), mediata sull'altezza del fascio.
    let meanBypassArea (bands: Band list) =
        if bands.IsEmpty then 0.0
        else bands |> List.averageBy (fun b -> b.BypassArea)

    /// Area del canale anulare **effettivamente aperto** in verticale, cioe' la
    /// corona compresa fra il diaframma di supporto e il mantello, mediata
    /// sull'altezza del fascio [m²/m di lunghezza assiale].
    ///   shellId  : diametro interno mantello [m]
    ///   baffleOd : diametro esterno del diaframma [m]  (>= shellId -> nessun canale)
    ///   otl      : diametro del fascio [m] (altezza su cui mediare)
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

    /// Verifica di consistenza: numero di tubi ricostruito
    let totalTubes (bands: Band list) = bands |> List.sumBy (fun b -> b.NTubes)
