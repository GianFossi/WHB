# Figure del Manuale Teorico WHB/PGC — sorgenti

28 figure, ciascuna disponibile in cinque formati. Scegliere il formato in base a come si
intende modificarla.

## Quale formato usare

| Cartella | Formato | Con cosa si modifica | Quando usarlo |
|---|---|---|---|
| `tex/` | LaTeX + TikZ | qualunque editor di testo, TeXstudio, Overleaf | **Sorgente vero.** Modifica parametrica: cambiare una quota, un colore, un'etichetta e ricompilare. E' il formato da tenere sotto controllo di versione |
| `svg/` | SVG vettoriale, testo editabile | Inkscape (gratuito), Illustrator, Affinity Designer, browser | Ritocchi grafici a mano: spostare un blocco, cambiare un testo, aggiustare una freccia senza ricompilare |
| `emf/` | Enhanced Metafile | Word, PowerPoint, Visio | Incollare in Office e modificare li dentro (in Word: click destro, Modifica immagine) |
| `pdf/` | PDF vettoriale | Illustrator, Inkscape, Acrobat Pro | Stampa e import in altri documenti senza perdita di qualita |
| `png/` | Bitmap 200 dpi | qualunque | Solo per anteprima o incollare dove serve un raster |

## Ricompilare una figura dal sorgente TikZ

Ogni file `.tex` e autonomo (classe `standalone`, non richiede il documento principale):

    xelatex nome_figura.tex

produce `nome_figura.pdf`. Da li:

    pdftocairo -svg nome_figura.pdf nome_figura.svg      # SVG
    pdftoppm -png -r 200 -singlefile nome_figura.pdf nome_figura   # PNG

Per SVG con testo editabile (non convertito in tracciati):

    dvisvgm --pdf --font-format=woff -o nome_figura.svg nome_figura.pdf

## Colori usati

I sorgenti definiscono cinque colori di riempimento, coerenti in tutte le figure:

| Nome | RGB | Uso nel manuale |
|---|---|---|
| `boxblue` | 225,238,252 | passi di calcolo, blocchi neutri |
| `boxgreen` | 225,247,231 | risultati, uscite, lato acqua |
| `boxorange` | 253,236,216 | elementi critici o di bypass |
| `boxgray` | 237,237,237 | ingressi, elementi passivi |
| `boxred` | 253,226,226 | livello piu interno dei cicli |

## Nota sulla coerenza con il manuale

Le figure incorporate nel file `MANUALE_WHB.docx` sono i PNG di questa cartella. Se si
modifica una figura qui, va rigenerato il PNG e sostituita l'immagine nel documento Word,
altrimenti manuale e sorgenti divergono.


## Indice delle figure per capitolo

| Capitolo | Figure |
|---|---|
| Front matter | `front_fig01_mappa_del_manuale` |
| 1 — Geometria e configurazione | `cap01_fig01_whb_orizzontale_con_steam_drum` |
| 4 — Ebollizione | `cap04_fig01_curva_di_ebollizione` |
| 10 — Flusso di calcolo del rating | `cap10_fig01` … `cap10_fig14c` (16 figure) |
| 11 — Core del solutore | `cap11_fig01` … `cap11_fig05` |
| 13 — Perdite di carico lato acqua | `cap13_fig01_anello_di_circolazione` |
| 14 — Reazione di shift | `cap14_fig01_inversione_entalpia_temperatura_shift` |
| 15 — Verifiche termo-meccaniche | `cap15_fig01_dilatazione_impedita_tubi_mantello` |
| 16 — Vibrazioni | `cap16_fig01_sensibilita_campata_vibrazioni` |

Le figure dei capitoli 14-16 usano `amsmath` oltre a `tikz`: se si ricompila un `.tex` con
un'installazione LaTeX minimale, verificare che il pacchetto sia disponibile.


## Cartella `_superate/`

Contiene figure di edizioni precedenti non piu usate nel manuale. Sono conservate perche
possono tornare utili quando il manuale verra esteso alle configurazioni future (generatore
a tubi verticali, double pipe verticali, OTC con coil concentrici), ma **non vanno inserite
nel documento corrente**: descrivono un ambito piu ampio di quello di questa edizione.
