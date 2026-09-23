# XSulfur — consolidamento e collegamento a Whb.Core

## Cosa vive dove

**Tutta** la chimica, termodinamica, proprietà e cinetica dello zolfo sta in
`XSulfur`. Non esistono copie altrove.

| File | Contenuto |
|---|---|
| `Speciation.fs` | equilibrio S1–S8, massa molare media, cp con polimerizzazione |
| `Chemistry.fs` | tensione di vapore, dew point, `condenserState`, supersaturazione |
| `LiquidProperties.fs` | viscosità attraverso la transizione λ, densità, tensione superficiale, k, cp |
| `FilmKinetics.fs` | film Nusselt, drenaggio del rivolo di fondo, hold-up, ri-trascinamento |
| `Checks.fs` | finestra di parete, fog, drenaggio, sulfidation, wet H2S |
| `CondensationCurve.fs` | import curve da simulatore, validazione, dT/dh, zone |
| `Condensation.fs` | ponte verso i metodi generici, k_G da Chilton-Colburn, h_fg |

**Silver-Bell-Ghaly e Colburn-Hougen NON sono metodi dello zolfo.** Restano in
`WhbThermo.TwoPhase` e `XSulfur.Condensation` li chiama. Duplicarli qui darebbe
due implementazioni che divergono — esattamente ciò che questo consolidamento
esiste per impedire. Un test verifica che il percorso zolfo e la chiamata
diretta diano lo stesso numero.

## Collegamento a Whb.Core

`src/Whb.Core/Materials/Sulfur/Sulfur.fs` è un **adattatore sottile**: converte
unità (Whb.Core lavora in Pa, XSulfur in bar) e nomi. Non contiene fisica.

Il `.fsproj` referenzia `XSulfur.fsproj` tramite la proprietà `WhbThermoRoot`,
che di default punta a un checkout fratello:

```
<parent>/WHB          questo repository
<parent>/WhbThermo    le librerie di proprietà
```

Override: `dotnet build -p:WhbThermoRoot=D:\src\WhbThermo`

Il pacchetto NuGet resta l'obiettivo finale; finché quella pipeline non esiste,
il project reference garantisce una sola sorgente di verità.

## Dati a runtime

`XSulfur` carica `sulfur-species.json` tramite `WhbThermo.Data.DataStore`:
`WHBTHERMO_DATA`, oppure una cartella `data` accanto all'eseguibile. Nulla è
embedded.
