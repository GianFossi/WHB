# WhbThermo — Process Gas Species Database & Property Engine

F# (.NET 8) library for Waste Heat Boiler / Process Gas Cooler thermal rating.
This first slice covers **the gas species database and pure-component / mixture
property evaluation**. Steam-water (IAPWS-IF97), heat transfer correlations and
ferrule hydraulics are separate projects to be added on the same skeleton.

## Design principles

1. **The database is data, not code.** `species-database.json` is embedded as a
   resource; no numeric constant is hard-coded in an F# `match`.
2. **Every number carries provenance and a validity range.** Each fit record has
   its own `source`, `tMinK`, `tMaxK`.
3. **Out-of-range never fails silently.** Correlations stay total: they return a
   value *plus a warning* through the `Returns<'T,'M>` warning channel. Warnings
   propagate all the way to the calculation report.
4. **Missing data fails loudly.** A species with only a 500 °C anchor point
   cannot produce an enthalpy — that call returns `Failure`, it does not guess.
5. **CoolProp is an oracle, not a dependency.** It runs offline in `tools/` to
   generate a frozen golden CSV; the library and its tests have no runtime
   dependency on it.

## Documentazione

| File | Contenuto |
|---|---|
| `docs/LIMITATIONS.md` | **lacune, limiti e sviluppi futuri** — leggere prima di fidarsi di un numero |
| `docs/XSULFUR.md` | consolidamento zolfo e collegamento a Whb.Core |

## Protezione del database

`data/species-database.json` è scritto da quattro strumenti diversi. Ogni
scrittura passa da `tools/db_guard.py`, che **rifiuta** una scrittura che perde
informazione — specie rimosse, proprietà perse, modello Cp degradato,
annotazione manuale caduta — con backup timestampato e hash di integrità.

```bash
python tools/db_guard.py    # stato, hash, ultime scritture
```

L'override `--force` esiste ma viene registrato nella storia del file. Il motivo
è in `docs/LIMITATIONS.md` §4: tre rigeneratori su quattro hanno già distrutto
il lavoro di un altro, e tutti e tre sono stati scoperti per caso.

## Layout

```
src/WhbThermo.Domain       Units of measure, species records, message DU
src/WhbThermo.Data         species-database.json + validating loader
src/WhbThermo.Properties   Sutherland, Shomate/NASA-7, Wilke / Wassiljewa mixing
src/WhbThermo.Radiation    WSGG / Leckner gas emissivity, linearised h_rad, total film
src/WhbThermo.Steam       IAPWS-IF97 regions 1, 2, 4
src/WhbThermo.Liquids     DIPPR-form liquid and vapour correlations, 30 species
src/WhbThermo.Convection  Gnielinski, Petukhov, Sieder-Tate, dP; Churchill-Chu, mixed convection
src/WhbThermo.Boiling     Cooper, Rohsenow, Palen, Zuber CHF, overall resistance
src/WhbThermo.TwoPhase    Chen, Steiner-Taborek, Shah, Boyko, Cavallini, SBG, Colburn-Hougen
src/XSulfur              Sulfur allotrope equilibrium and imported condensation curves
data/                     all reference data, loaded at runtime (NOT embedded)
tests/WhbThermo.Tests      xUnit + golden-file regression
tools/build_database.py    regenerates the JSON from source tables
tools/generate_reference.py  regenerates the CoolProp golden CSV
tools/nasa7.py             NASA-7 / CHEMKIN fixed-column parser (self-testing)
tools/merge_burcat.py      merges NASA-7 coefficients into the database
tools/validate_leckner.py  structural + chart validation of the Leckner matrices
tools/validate_wsgg.py     validation of the Smith 1982 WSGG coefficients
tools/fit_leckner.py       least-squares fit of Leckner-form coefficients
tools/nasa9.py             NASA-9 (CEA/PAC/Burcat) parser + coverage scanner
tools/verify_if97.py       IF97 check against the official verification tables
tools/cea_transport.py     NASA CEA trans.inp parser (viscosity + conductivity)
tools/import_cea.py        builds the species database from the CEA reference data
tools/crosscheck_sources.py  validates the shipped data against an independent source
tools/generate_golden.py   regenerates all golden reference data (CoolProp, Cantera, iapws)
tools/import_dippr.py      builds the liquid database from open DIPPR-form tables
tools/verify_dippr.py      screens every liquid correlation against CoolProp
tools/verify_critical.py   checks the critical constants against CoolProp
```

## Current data status

28 species, **all production grade**. Sourced from the NASA CEA reference data
(github.com/nasa/cea, **Apache 2.0** — no commercial-use restriction, unlike the
Goos–Burcat–Ruscic database).

| | Count | Model |
|---|---|---|
| Cp | **28** | NASA-9 polynomials, `thermo.inp`, to 6000 K |
| Transport | **20** | NASA CEA 4-parameter, `trans.inp`, to 5000 K |
| Transport fallback | 8 | Sutherland from the manual (S₂, S₆, S₈, C₃H₆, C₃H₈, C₆H₆, C₇H₈, SO₃) |

Rebuild with:

```bash
git clone https://github.com/nasa/cea.git
python tools/import_cea.py cea/data
```

### Confirmed against a second independent compilation

The shipped Cp data (NASA CEA, Apache 2.0) has been cross-checked against the
Goos–Burcat–Ruscic database in CHEMKIN NASA-7 form. That database's licence
forbids inclusion in commercial software, so it is used **only as a validator**:
`crosscheck_sources.py` reads a file you supply, compares, and reports. Nothing
from it is written into `species-database.json`.

```bash
python tools/crosscheck_sources.py /path/to/THERM_DAT.txt
```

Result over 400–1673 K: **22 of 28 species agree within 1 %**, Ar and He exactly.
Six show larger deviations, all concentrated at the **cold end**:

| Species | 400 K | 800 K | 1400 K |
|---|---|---|---|
| C₆H₆ | −13.9 % | −1.9 % | +0.9 % |
| C₇H₈ | −13.0 % | −2.5 % | −0.2 % |
| CO₂ | −5.3 % | −1.6 % | +3.6 % |
| NO₂ | −6.8 % | −3.5 % | +0.2 % |
| N₂O | −5.9 % | +1.5 % | +6.0 % |
| C₃H₆ | +7.1 % | −1.7 % | −1.4 % |

400 K is below anything a WHB gas side sees — a TLE outlet is around 570 K — so
in service the two compilations agree to a few percent everywhere. Treat the
table as a map of where the fits are weakest, not as a defect list.

### Two findings from the import, both worth knowing

**1. `Co` is cobalt; `CO` is carbon monoxide.** Keying the parsed records
case-insensitively silently replaced one with the other and produced a **21 %
error in CO's Cp** that looked entirely plausible. `nasa9.find` now matches
exactly first and only falls back to a case-insensitive match when it is
unambiguous. The same trap catches isomers: `C2H2,acetylene` is the species you
want, `C2H2,vinylidene` is not.

**2. The source manual's Cp @ 500 °C column is wrong for every hydrocarbon.**
Settled by a four-way comparison at 773.15 K:

| Species | NASA CEA | Goos–Burcat–Ruscic | NIST Shomate | Manual |
|---|---|---|---|---|
| CH₄ | 3.901 | 3.906 | 3.839 | **2.850** (−27 %) |
| C₂H₄ | 2.933 | 2.940 | 2.935 | **2.380** (−19 %) |
| C₂H₆ | 3.514 | 3.520 | — | **2.760** (−22 %) |
| C₃H₈ | 3.439 | 3.445 | — | **2.820** (−18 %) |
| C₆H₆ | 2.387 | 2.441 | — | **1.850** (−23 %) |
| C₇H₈ | 2.492 | 2.564 | — | **1.900** (−24 %) |

kJ/(kg·K). Three independent compilations agree to within 0.2 % on most of these;
the manual reads 15–27 % low on **every** hydrocarbon and within 0.1–7 % on
everything else. Those values look like Cp near 150 °C rather than 500 °C. The
importer validates against NIST Shomate where available and treats an anchor
mismatch as a defect in the manual, not as grounds to reject NASA data. Relevant
if anyone has rated ethylene TLE service from that table — the computed duty
would be optimistic.

## How to complete the remaining data

1. **Cp — preferred route (implemented).** Get a Burcat/CHEMKIN thermo file, then:

   ```bash
   python tools/merge_burcat.py path/to/BURCAT.THR --dry-run   # inspect
   python tools/merge_burcat.py path/to/BURCAT.THR             # merge
   ```

   This closes all 13 pending species in one pass. Any CHEMKIN-format mechanism
   works — the Claus/sulfur mechanisms cover S₂/S₆/S₈, COS and CS₂ better than
   NIST does. `tools/nasa7.py` is the parser; run it directly for its round-trip
   self-test.

   Two guard-rails are built in:
   * every merged record is checked against the manual's 500 °C anchor and
     **rejected** beyond 15 % deviation — this catches a wrong species alias or a
     swapped high/low coefficient block, the classic NASA-7 mistake;
   * `tools/build_database.py` preserves merged NASA-7 blocks, so regenerating
     the base tables never silently undoes a merge.

   `tools/fixtures/sample-thermo.dat` is a synthetic file that exercises the
   pipeline end-to-end. It is **not** real thermochemistry — do not merge it.
2. **Cp — manual route.** NIST WebBook, one species at a time. Copy A–H plus the
   temperature range into `SHOMATE` in `build_database.py`, then re-run it.
3. **Critical properties.** Poling, *The Properties of Gases and Liquids*, 5th ed.,
   Appendix A. Needed once you add a real-gas EoS (Z ≠ 1 matters above ~50 bar).
4. **After every addition**, lower the `allowed` constant in
   `DatabaseTests.pending-completion count does not regress`. That test is the
   ratchet that keeps the database moving forward.
5. **Prefer NASA-7 over Shomate where both exist.** NASA-7 fits normally run to
   5000–6000 K, well past the 1400 °C inlet of an SRU or reformer WHB, whereas
   most NIST Shomate ranges stop near 1200–1400 K. Run
   `merge_burcat.py --all` once you trust the source file.
6. **High-temperature transport.** Above ~1000 °C the Sutherland fits degrade.
   When that becomes the binding error, add a `TransportModel` DU case for
   Chung or Lucas rather than re-fitting Sutherland.

## Assumption to check on first build

The ROP `open` statement and combinator names are written as:

```fsharp
open Ganfoss.ROP
// ok, fail, warn, warnIf, traverseList, (>>=)
```

If your actual namespace or `warnIf` argument order differs, fix it once in
`Messages.fs` / `Loader.fs` — nothing else depends on the shape.

`FSharp.SystemTextJson` is referenced so the DU-free DTOs deserialise cleanly;
drop it if you prefer plain `System.Text.Json` with `[<CLIMutable>]` only.

## Usage

```fsharp
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

// Claus SRU waste heat boiler inlet gas
let result =
    SpeciesDatabase.loadEmbedded ()
    >>= fun db -> Mixing.fromKeys db [ "H2S", 0.05; "SO2", 0.02; "N2", 0.60
                                       "H2O", 0.28; "CO2", 0.05 ]
    >>= fun mix -> Mixing.evaluate mix 1200.0<K> 1.5<bar>

match result with
| Success (props, warnings) ->
    printfn "Pr = %.3f, mu = %.3e Pa.s" props.Prandtl (float props.Viscosity)
    warnings |> List.iter (fun w -> eprintfn "WARNING: %O" w)
| Failure msgs ->
    msgs |> List.iter (fun m -> eprintfn "ERROR: %O" m)
```

## Build

```bash
dotnet build WhbThermo.sln
dotnet test  WhbThermo.sln
```

Not compiled in the environment where it was generated (no NuGet access), so
expect a few small fixes on the first `dotnet build`.


## Gas radiation (WhbThermo.Radiation)

At a 1400 °C SRU or reformer inlet, participating-gas radiation is not a
correction — it is a large fraction of the internal film coefficient. The module
implements the Leckner (1972) formulation used by the manual:

```
log10 eps0 = SUM_i a_i (log10 pL)^i        a_i = SUM_j c[j][i] (T/1000)^j
eps_g      = eps_H2O + eps_CO2 - delta_eps_overlap
eps_eff    = [ 1/eps_g + 1/eps_tube - 1 ]^-1
h_rad      = eps_eff * sigma * (Tg^4 - Tw^4) / (Tg - Tw)
```

The whole model reduces to one coefficient matrix per species, so the matrix is
**data**: its shape is read from `radiation-models.json`, since H₂O and CO₂ use
different polynomial orders.

### Two models, one usable out of the box

| Model | Status | Use |
|---|---|---|
| **Smith, Shen & Friedman (1982) WSGG** | **populated** | default — works with no data entry |
| Leckner (1972) chart correlation | coefficients empty | fill only if you specifically need it |

```fsharp
RadiationModel.loadDefault ()      // -> SmithWsgg, ready to evaluate
```

The WSGG model gives total emissivity **and** the Beer–Lambert absorption
coefficient `k = -ln(1-ε)/L`, which is what a marching or finite-volume solver
actually needs — the Leckner chart correlation only gives ε.

```
eps    = SUM_i a_i(T) (1 - exp(-k_i p L))     a_i(T) = SUM_j b[i][j] T^(j-1)
a_0    = 1 - SUM_i a_i                        (clear gas)
```

Validity: T 600–2400 K, p·L 0.001–10 atm·m, total pressure 1 atm. The WHB
envelope (250–1400 °C) sits inside the temperature range; short tube beam
lengths sit near the lower p·L bound, which raises a warning.

Coefficients: T.F. Smith, Z.F. Shen, J.N. Friedman, *J. Heat Transfer* **104**
(1982) 602–608, as tabulated by Marzouk & Huckaby, 7th US National Combustion
Meeting (2011), NETL/DOE, Table 7 (arXiv:2411.18467). Composition blending uses
the piecewise-**linear** scheme of that paper's Appendix B, which it shows to be
materially more accurate than piecewise-constant at low H₂O fractions.

Verified by `tools/validate_wsgg.py`: gray-gas weights physical across
600–2400 K (minimum clear-gas weight 0.244), monotonic in p·L, continuous across
the RR = 1/2 and 2/3 blending boundaries (jumps < 4e-5), and magnitudes matching
the Hottel chart order (ε = 0.313 at 1200 K, p·L ≈ 0.43 atm·m).

### Leckner-form model: fitted, not transcribed

The original Leckner (1972) coefficient tables sit behind a paywalled journal and
are not reproduced in any open source we could verify. Rather than type them from
memory, `tools/fit_leckner.py` fits the **same functional form** by ordinary
least squares and labels the result honestly in the JSON `source` field.

```
log10(eps_i) = SUM_i a_i (log10 pL)^i          a_i = SUM_j c[j][i] (T/1000)^j
delta_eps    = zeta(1-zeta) * SUM_j SUM_m [ C[j][m] + CAsym[j][m] zeta ]
                              * theta^j (log10 pL_tot)^m
```

Because `log10(eps)` is linear in `c`, this is ordinary least squares — no
iteration, no local minima. The `zeta(1-zeta)` prefactor enforces structurally
that band overlap vanishes when either species is absent; `CAsym` carries the
asymmetry about `zeta = 0.5`, which is significant at CO₂-rich compositions.

Current fit, calibrated against the validated Smith WSGG model:

| | points | mean error | max error |
|---|---|---|---|
| H₂O | 121 | 4.0 % | 15.1 % |
| CO₂ | 121 | 3.0 % | 8.6 % |
| overlap (p·L ≤ 1 atm·m) | 605 | 0.009 abs | 0.074 abs |

Cross-checked against the oracle at WHB conditions: agreement within 15 %,
typically 10 % (`fitted Leckner tracks the WSGG oracle` test).

**What this is and isn't.** These coefficients are a Leckner-*form*
reparametrisation of Smith WSGG. They are useful when a calculation must be
presented in the chart-correlation format, but they carry **no accuracy
independent of WSGG** — do not treat agreement between the two as validation.
To make them independent, refit against a line-by-line or SNB dataset:

```bash
python tools/fit_leckner.py --csv reference.csv   # species,T_K,pL_atm_m,emissivity
```

A suitable open dataset is the EM2C SNB total emissivity set (300–2900 K,
p·L 0.01–50 atm·m) published in *Data in Brief*.

**Worth knowing before you rely on Leckner at all.** Alberti, Weber and Mancini
report that emissivities from Leckner's own polynomials can differ from
line-by-line HITEMP-2010 calculations by −90 % to +180 %. The chart correlations
are a 1970s artefact; WSGG is the better default, which is why
`RadiationModel.loadDefault()` returns it.

### A validator finding worth recording

`validate_leckner.py` initially flagged CO₂ emissivity as rising with
temperature between 800 and 1000 K. That turned out to be **correct physics, not
a bad fit** — CO₂ emissivity genuinely peaks near 900–1000 K, confirmed against
the reference data. The monotonic-decrease check now starts at 1200 K. Recorded
here because the same false positive will recur for anyone refitting.

### Numerical note

`radiativeCoefficient` handles the removable singularity of
`(Tg⁴ − Tw⁴)/(Tg − Tw)` analytically, returning the limit `4 ε σ T³` as
Tg → Tw. Without this a marching solver divides by zero the moment a tube
approaches equilibrium with the gas — which happens routinely in the cold end of
a long WHB.

### Usage

```fsharp
RadiationModel.loadDefault ()
>>= fun model ->
    InternalFilm.total model hConv tGas tWall yH2O yCO2 pressure
                       (Emissivity.CircularDuct dInt) tubeEmissivity true
```

At SRU inlet conditions (1400 °C gas, 350 °C wall, 28 % H₂O, 5 % CO₂, 1.5 bar,
50 mm tube) this gives ε ≈ 0.040 and h_rad ≈ 16 W/(m²·K) — small next to a
typical 120 W/(m²·K) convective film, because the beam length inside a 50 mm tube
is only 47 mm. Radiation matters far more in large-bore inlet channels and
chambers than inside the tubes themselves.

Pass `false` for the radiation flag to run convection-only; the result then
carries an explicit warning that it is non-conservative above ~700 °C.


## Data loading: external files, not embedded resources

Nothing is compiled into the assemblies. `WhbThermo.Data.DataStore` resolves and
caches files from disk:

```fsharp
DataStore.setRoot "/etc/whbthermo/data"     // optional, highest priority
SpeciesDatabase.load ()                     // resolves species-database.json
```

Resolution order: `setRoot` → `WHBTHERMO_DATA` environment variable → a `data`
folder beside the assembly → `data` under the working directory.

Three reasons this is worth the indirection:

1. **Licensing.** Several thermodynamic databases — Goos–Burcat–Ruscic among
   them — are free for non-commercial use but forbid redistribution *inside*
   software. Data outside the assembly means the tool never carries it.
2. **Field maintenance.** A coefficient can be corrected on a running
   installation by editing a file. The cache invalidates on last-write time, so
   the change takes effect on the next call with no restart.
3. **Auditability.** `DataStore.loaded ()` returns the path and timestamp of
   every file used, which a calculation report can cite. An embedded blob can't.

A failed parse is not cached, so a corrupt file that is then fixed reloads
cleanly rather than sticking as a permanent failure.

## Steam and water: IAPWS-IF97

`WhbThermo.Steam.If97` implements regions **1** (compressed liquid), **2**
(superheated steam) and **4** (saturation line). Regions 3 (near-critical) and 5
(above 1073 K) are deliberately not implemented — a WHB drum at 40–120 bar never
enters them — and states falling there **fail explicitly** rather than
extrapolating a valid equation into a range where it is meaningless.

```fsharp
If97.load ()
>>= fun m -> If97.saturatedAt m 100.0<bar>
// -> Tsat 311 degC, latent heat 1317 kJ/kg, rho_l 688, rho_v 55 kg/m3
```

This is the one correlation in the repository that can be checked against
**printed digits instead of judgement**: IF97 ships with official verification
tables. `tools/verify_if97.py` reproduces all of them — regions 1, 2, and both
saturation equations — to better than 1e-9 relative, driven purely by the JSON
so it validates the data file rather than a hard-coded copy.

Why this beats the empirical fits in most vendor manuals (Wagner + polynomial
correlations for rho, mu, k, cp): those are individually fitted and therefore
**not thermodynamically consistent** across the saturation line, typically 1–2 %
off and mutually contradictory at the margins. IF97 is the international
industrial standard, is consistent by construction, and regions 1/2/4 are
explicit so it is also faster than the iterative alternatives.

Coefficient counts are validated on load (34 / 9 / 43 / 10 terms). A file with
the wrong count is rejected whatever its header claims.


## Test campaign

```bash
pip install CoolProp cantera iapws
python tools/generate_golden.py     # regenerates tests/WhbThermo.Tests/reference/
dotnet test WhbThermo.sln
```

Three oracles, each used only for what it is actually authoritative on. The
generated CSVs are committed and frozen, so none of these packages is a build or
test dependency.

| Oracle | Validates | Points | Result |
|---|---|---|---|
| CoolProp | pure-species cp, mu, k | 259 comparisons, 13 species | worst cp 2.3 %, mu 2.9 %, k 11.6 % |
| Cantera | the mixing rule, in isolation | 45 mixture states, 9 compositions | Wilke viscosity **0.0000 %** |
| iapws | IF97 grid and saturation line | 26 states + 11 drum pressures | 1e-6 relative |

### Why the mixing rule is tested separately

An end-to-end mixture comparison conflates two different defects: bad species
data and a bad mixing rule. The golden file therefore records **Cantera's own**
pure-species mu and k alongside its mixture result. Feeding those pure values
into `Mixing.combine` must reproduce Cantera's mixture value, because at that
point both sides evaluate the same formula on identical inputs. It does, to
0.0000 % — the Wilke implementation is exact.

Conductivity cannot be checked the same way: Cantera implements
Mathur–Tondon–Saxena, a different model from the Wassiljewa formulation used
here. What *is* asserted rigorously is that our result stays inside the bounds
any admissible mixture conductivity must satisfy:

```
1 / SUM(x_i / k_i)   <=   k_mix   <=   SUM(x_i k_i)
```

45 of 45 cases comply. The spread between the two models reaches 14 % on
H₂-rich mixtures — genuine model uncertainty, worth knowing before quoting a
mixture conductivity to three figures.

### Three findings from building the campaign

**1. The Mason–Saxena factor does not belong on this φ.** The manual specifies
"Wassiljewa with Mason & Saxena modification". Applying that ε = 1.065 on top of
the viscosity-ratio φ_ij the manual itself writes out drives k_mix **below the
rigorous lower bound in 22 of 45 cases**. The genuine Mason–Saxena form uses the
ratio of monatomic *translational* conductivities, not of viscosities, so ε does
not belong there. We use ε = 1 and stay inside the bounds.

**2. The oracle needs validating too.** CoolProp returned k = 0.0017 W/(m·K) for
ammonia at 1000 K — about two orders of magnitude low — with no error raised.
`generate_golden.py` now screens reference points two ways: a 25 % margin above
the 1 bar saturation temperature, and a monotonicity check on the reference
series itself, since gas viscosity and conductivity rise with temperature. A
reference point that falls is the oracle failing, not our correlation. 53 points
were discarded this way.

**3. Loose tolerances are worthless.** A negative-control test evaluates N₂
against CO₂'s reference rows and asserts the comparison *fails*. It rejects 7 of
9 rows, confirming the 3 % cp gate is tight enough to catch a swapped species.

### Coverage

- Pure species: all 28, at 100–1400 °C, checked for finiteness, positivity and
  monotonic viscosity.
- Mixtures: eight process compositions from the manual — Claus SRU, SMR syngas,
  ammonia synloop, ethylene TLE, FCC flue gas, nitric acid, sulfur vapour, DRI
  reformer — evaluated at 250–1400 °C with Prandtl and viscosity plausibility
  gates. The Claus and sulfur-vapour cases have no Cantera counterpart (gri30
  carries no H₂S or SO₂), so they are covered end to end only.
- Continuity: mixture cp, mu and k are checked for jumps where the NASA-9 and
  NASA transport intervals join at 1000 K and 1073.2 K. A discontinuity there
  would destabilise any marching solver downstream.
- Steam: saturation line at 5–160 bar with monotonicity of Tsat, both densities
  and latent heat, plus the identity h_fg = h_v − h_l at every pressure.


## Shell-side boiling (WhbThermo.Boiling)

Cooper and Rohsenow pool boiling, Palen bundle corrections, Zuber critical heat
flux, and the overall resistance network referred to the outside tube area.

Surface tension comes from **IAPWS R1-76** and reproduces the published values
to better than 0.1 % from 20 to 300 °C.

### Cooper is the default, and here is why

At 100 bar and 5 K superheat, the published water values of Rohsenow's C_sf
produce:

| C_sf | h [kW/(m²·K)] |
|---|---|
| 0.0060 (brass) | 1108 |
| 0.0080 (ground stainless) | 467 |
| 0.0130 (copper) | 109 |
| 0.0132 (mech. polished SS) | 104 |
| **Cooper** | **275** |

A factor of **10.6** from the surface constant alone, because C_sf enters cubed.
Cooper needs only reduced pressure and molar mass and carries about ±30 %
scatter. Use Rohsenow only when C_sf was measured on the actual surface.

### The critical heat flux gate

Both correlations are nucleate-regime only and neither knows where that regime
ends. Cooper's superheat form at 100 bar gives:

| Superheat | q″ | vs CHF (3.77 MW/m²) |
|---|---|---|
| 3 K | 0.29 MW/m² | 8 % — fine |
| 10 K | **11.2 MW/m²** | **298 % — past burnout** |

So `checkAgainstCritical` returns **Failure**, not a warning, once the flux
reaches the critical value: past it the surface dries out, the film coefficient
collapses by an order of magnitude and the wall runs away to gas temperature. It
warns above 70 % of nominal, because with ±30 % correlation scatter the error
bands already touch there.

Zuber CHF reproduces the known behaviour for water: a maximum of **3.95 MW/m² at
60 bar**, falling away on both sides. `bundleCriticalHeatFlux` applies the
Palen–Small derating — a dense bundle burns out far below a single tube, and
ignoring it is a classic way to design a boiler that tests fine on paper and
dries out in service.

### Where the resistance actually sits

For a typical WHB tube (44/50.8 mm, h_gas 150, h_boil 25000, Rf 0.0004/0.0002):

| Term | Share |
|---|---|
| gas film | **90.6 %** |
| inner fouling | 5.4 % |
| outer fouling | 2.4 % |
| wall | 1.1 % |
| boiling | 0.5 % |

U_ext = 118 W/(m²·K). This is the number that should govern where effort goes:
halving the boiling coefficient moves U by 0.25 %, while the fouling assumption
moves it by several percent, and the gas-side film by almost everything.
`Resistances.Shares` returns this breakdown for exactly that reason.

A zero-fouling call raises a warning that it is the **clean case** — valid for
metal temperature checks, not for a duty guarantee.


## In-tube convection (WhbThermo.Convection)

Gnielinski is the default; Petukhov supplies the friction factor and the
pressure drop. Dittus-Boelter and Sieder-Tate are retained for reproducing
legacy specifications and always warn.

Textbook check at Re = 50000, Pr = 0.7: f = 0.020958, Nu = 104.2 (Gnielinski),
Nu = 118.7 (Dittus-Boelter) — the expected 14 % spread between them.

### The property-variation correction, and a trap

At an SRU inlet the gas is near 1400 °C and the wall near 350 °C, a ratio of
2.7, while every correlation is fitted on near-isothermal data. The obvious
move is a temperature-ratio correction — and the obvious form of it is wrong:

| Treatment | Factor | h_conv W/(m²·K) |
|---|---|---|
| No correction | 1.000 | 144.5 |
| Kays, gas **cooled**, n = 0 | 1.000 | 144.5 |
| Sieder-Tate (μ_b/μ_w)^0.14 | 1.110 | 160.4 |
| Properties at film temperature | 1.000 | 124.2 |
| naive (T_b/T_w)^0.45 | **1.560** | **225.3** |

A naive exponent gives a **56 % enhancement** — in the non-conservative
direction, on the hottest tube in the boiler. Kays gives n = −0.5 for gas
*heating* and approximately **0 for gas cooling**, and a waste heat boiler cools.
So `KaysTemperatureRatio` with `GasCooled` deliberately returns 1.0, and
`PropertyCorrection.IsEnhancement` exposes the direction. Any factor above 1.15
raises a warning.

The two defensible treatments for a gas cooler are Sieder-Tate (mild, +11 %
here) or evaluating properties at the film temperature with no ratio at all —
`filmTemperature` is provided for the latter. Note that they do not agree: 160
against 124 W/(m²·K), a 25 % spread. That is the real uncertainty in the
gas-side film, and it dominates the boiling correlation choice by a wide margin.

### Regimes

The transition band 2300 < Re < 3000 has no reliable correlation. The result is
interpolated between the laminar Nusselt and Gnielinski at Re = 3000 and warned
about — not accurate, but continuous, which a hard switch is not: the joins step
by 0.26 % and 0.13 %. Laminar flow in a WHB tube raises its own warning, since
it is usually a symptom of maldistribution rather than a design condition.

## The chain, closed

Claus SRU inlet — 1400 °C gas, 1.5 bar, 44/50.8 mm tube, 6 m, G = 25 kg/(m²·s),
wall 350 °C, Rf 0.0004 / 0.0002:

| Step | Result |
|---|---|
| Mixture properties | M 27.14, ρ 0.293 kg/m³, μ 59.3 μPa·s, k 0.122 W/(m·K), Pr 0.748 |
| Reynolds | 18 550, turbulent |
| Gnielinski + entrance | h_conv 145 W/(m²·K) |
| WSGG radiation | ε 0.038, h_rad 15.8 W/(m²·K) |
| Internal film | 161 W/(m²·K) |
| Overall | **U_ext ≈ 145 W/(m²·K)**, q″ ≈ 152 kW/m² |
| Pressure drop | 39 mbar over 6 m |

Every step is covered by the test campaign. The remaining gap in the rating
chain is the ferrule flow distribution, which decides how far the hottest tube
departs from this average.


## Natural and mixed convection (Convection.Buoyancy)

Churchill & Chu for vertical surfaces and horizontal cylinders, the Gr/Re²
criterion, and Chen/Churchill cubic combination for the mixed regime.

Textbook checks: vertical plate at Ra = 1e9, Pr = 0.7 gives Nu = 122.6;
horizontal cylinder at Ra = 1e6 gives Nu = 14.51.

### Where buoyancy actually matters — and where it does not

Gr scales as **ρ² D³**, so density and bore decide everything. Running the four
WHB services through the criterion:

| Service | P bar | D m | ρ kg/m³ | Gr | Gr/Re² at design | Mixed regime reached at |
|---|---|---|---|---|---|---|
| SRU Claus tube | 1.5 | 0.044 | 0.29 | 1.3e4 | 0.00004 | **1.9 % load — never** |
| SMR PGC tube | 25 | 0.044 | 3.3 | 2.5e6 | 0.0014 | 12 % load |
| Ammonia synloop tube | 200 | 0.050 | 34.9 | 3.7e8 | 0.028 | **53 % load** |
| Flue gas inlet channel | 1.1 | 0.90 | 0.36 | 1.7e8 | 0.007 | 26 % load |

This corrects an assumption made earlier in this project. Buoyancy is **not** a
general turndown concern: in a small low-pressure tube it is irrelevant at any
credible flow, because a 0.29 kg/m³ gas simply has no weight to redistribute.
Where it does bite is high pressure — the ammonia synloop tube enters the mixed
regime at **half load**, well inside normal operation — and large bores, where
D³ does the work instead.

### Direction is the part that is easy to get wrong

In a vertical tube with gas being **cooled**, the gas at the wall is denser than
the core, so buoyancy drives it **downward**. That opposes upward flow. The
combination is therefore

```
Nu³ = Nu_forced³ ± Nu_natural³      + assisting, − opposing
```

and for opposing flow the combined coefficient falls **below** the forced value.
Treating buoyancy as always helpful is optimistic in precisely the case a WHB is
most likely to be in. When the opposing natural contribution exceeds the forced
one, the near-wall flow is reversing and `combine` returns **Failure** — no
correlation is valid there, and reporting a number would be worse than refusing.

The evaluation also warns while still *forced-dominated* once Gr/Re² passes
0.05, so a rating flags that it is about to become invalid at turndown rather
than after.


## Liquids (WhbThermo.Liquids)

Temperature-dependent DIPPR-form correlations for 30 species: water, ammonia,
the sulfur and nitrogen compounds, and the hydrocarbon series C1–C10 plus
aromatics and alcohols.

### Where the data come from

Full DIPPR 801 is an AIChE subscription product and cannot be retrieved. Neither
can NIST TDE or PPDS. What is open is the subset published in the literature:
Perry's Handbook 8th ed. Tables 2-150, 2-153, 2-312 to 2-315, and the VDI Heat
Atlas PPDS polynomials — both redistributed by the `chemicals` package under MIT.

```bash
pip install chemicals
python tools/import_dippr.py
python tools/verify_dippr.py
```

Coverage: viscosity, conductivity, density, heat capacity, heat of vaporisation
and vapour transport for all 30 species (water has no DIPPR 105 density record —
IF97 covers it, and the call fails explicitly rather than returning zero).

### Accuracy against CoolProp, in range

453 comparisons, graded only inside each correlation's own validity range:

| Property | Worst deviation |
|---|---|
| density | 0.8 % |
| heat of vaporisation | 3.8 % |
| heat capacity | 6.0 % |
| conductivity | 9.5 % |
| viscosity | 49 % — see below |

### Three defects the verification found

**1. DIPPR 105 is tabulated in mol/m³**, not the kmol/m³ the equation definition
implies. A factor of 1000 that produces an entirely plausible-looking answer.

**2. Perry Table 2-150 puts the critical temperature ahead of the coefficients**
and is in kJ/kmol. Reading it positionally gives a heat of vaporisation with the
right shape and the wrong magnitude.

Both were caught only by comparing against CoolProp, not by reading headers. The
importer now records the equation number and output unit with every coefficient
set, and the conversion happens inside the library rather than at the call site.

**3. The n-pentane liquid viscosity record is bad.** It deviates 19 % to 49 %
across its *entire* tabulated range, while n-butane and n-hexane from the same
table stay within 2 %, and its coefficient structure differs from its homologues
(C₃ positive, C₁ an order of magnitude larger). A species wrong everywhere while
its neighbours are right is a bad table entry, not a correlation limitation. It
is flagged `suspect` in the data file with the reason, excluded from the golden
test with that exclusion documented, and a test pins the flag so a future
re-import cannot silently drop it.

`verify_dippr.py` applies that logic generally: a deviation confined to the top
of the range is the fit running out; a deviation across the whole range is a
suspect record.


## Two-phase (WhbThermo.TwoPhase)

Flow boiling and in-tube condensation, including the multicomponent case.

### Flow boiling: Chen against Steiner-Taborek

Water at 10 bar, G = 300 kg/(m²·s), D = 25 mm, 5 K superheat, q = 50 kW/m²:

| x | Chen | Steiner-Taborek | ratio | Chen nucleate share |
|---|---|---|---|---|
| 0.05 | 11 400 | 24 200 | **2.12** | 33 % |
| 0.20 | 17 800 | 28 100 | 1.58 | 9 % |
| 0.60 | 30 300 | 41 100 | 1.35 | 2 % |

W/(m²·K). The two disagree by a **factor of two at low quality** and converge as
convection takes over. The reason is structural: Chen *adds* the two mechanisms
with a suppression factor that crushes the nucleate term (S = 0.29 at x = 0.05,
0.05 at x = 0.6), while Steiner-Taborek combines them **asymptotically** as a
cube root, so the larger one dominates smoothly without being suppressed.

Neither is right. The choice of correlation is a larger effect than anything in
the property data feeding it, and the tests pin the size of the gap so a future
change cannot quietly move it.

Steiner-Taborek needs a fluid-specific reference coefficient h_nb,o measured at
q = 20 kW/m² and pr = 0.1 — 25 580 W/(m²·K) for water. There is no way to derive
it, and borrowing one from another fluid is the main way the correlation goes
wrong, so it is a required argument rather than a default.

### Condensation

Shah, Boyko-Kruzhilin and Cavallini-Zecchin span **36 %** at mid quality.

Shah and Boyko-Kruzhilin are two-phase multipliers on the all-liquid
coefficient, so both return exactly h_lo at x = 0 — a useful check that is
implemented as a test. Cavallini-Zecchin is an independently fitted correlation
with a different leading constant (0.05 against 0.023) and Prandtl exponent, so
it does **not** reduce to the same limit. Asserting that it should would be
asserting a misunderstanding, so the test asserts the opposite.

### Silver / Bell & Ghaly: the effect that governs mixture condensers

```
1/h_eff = 1/h_cond + Z/h_gas        Z = x cp_g (dT/dh)
```

With h_cond = 8000 and h_gas = 200 W/(m²·K):

| Z | h_eff |
|---|---|
| 0 (pure vapour) | 8000 |
| 0.05 | 2667 |
| 0.20 | **889** |
| 1.00 | 195 |

A Z of only 0.2 costs nearly an order of magnitude, because the gas-phase
resistance takes over. This is the largest single effect in condensing a
hydrocarbon cut and the classic reason a condenser comes up short in service.
Above Z = 0.5 the module warns that the result is governed by the vapour-side
estimate and is correspondingly uncertain.

Z comes from the condensation curve — a flash calculation, not a correlation —
so `zFactor` takes dT/dh as an argument rather than inventing it.

### Colburn & Hougen with non-condensables

The interface energy balance

```
h_g(T_g - T_i) + k_g M_v h_fg ln[(P - p_i)/(P - p_v)] = h_film(T_i - T_w)
```

is exposed as a **residual** plus a bisection solver, because the interface
vapour pressure is the saturation pressure of the mixture at T_i and that
requires a flash the caller owns. Bisection rather than Newton: the logarithm
stiffens sharply as the interface approaches the bulk composition, and a Newton
step there overshoots out of the physical range. An unbracketed case is refused
rather than silently returning an endpoint.


## XSulfur — Claus sulfur condensers

A separate library, because sulfur is not a component but a reacting mixture.

### What exists already

| Tool | Nature |
|---|---|
| Sulsim (Sulfur Experts, in HYSYS), ProMax, Symmetry | commercial, the industry standard |
| ProSim Claus example | uses Gibbs minimisation; notes DIPPR lacks S₂/S₆/S₈, built from literature |
| WMD-group/sulfur-model (GitHub) | open, DFT-based S₁–S₈ equilibrium — aimed at semiconductor sulfurisation, not Claus conditions |
| Gamson & Elkins (1953), Paskall (1979) | the classic speciation references the Claus literature still cites |

No open library targets Claus condensers. But the NASA CEA data already imported
here contain **all eight allotropes**, S₁ through S₈, so the equilibrium can be
solved from Gibbs energies directly.

### Speciation from first principles

`n/2 S₂ ⇌ Sₙ`, solved by bisection on log p_S₂. At p_S = 0.05 bar:

| T (K) | T (°C) | M avg | S₈ | S₆ | S₂ | cp_eff / cp_frozen |
|---|---|---|---|---|---|---|
| 450 | 177 | 247 | 0.83 | 0.11 | 0.00 | 1.33 |
| 550 | 277 | 233 | 0.58 | 0.28 | 0.00 | 1.57 |
| 650 | 377 | 208 | 0.31 | 0.40 | 0.05 | **2.95** |
| 700 | 427 | 180 | 0.18 | 0.36 | 0.18 | **5.87** |
| 800 | 527 | 89 | 0.01 | 0.07 | 0.74 | **18.5** |
| 900 | 627 | 67 | 0.00 | 0.00 | 0.94 | 2.78 |

This reproduces the published Claus behaviour — S₆/S₈ dominant below 700 °F,
S₂ above 1000 °F — from CEA Gibbs energies alone, with no fitted parameters.

**The last column is the point.** `effectiveHeatCapacity` differentiates the
equilibrium enthalpy per mole of *sulfur atoms*; `frozenHeatCapacity` holds the
composition fixed. Their ratio is the polymerisation contribution, and it reaches
**18×** at 527 °C. A rating that treats sulfur as a pseudo-component with a fixed
molecular weight discards all of it. Referring enthalpy to atoms rather than
molecules is what makes the reaction term appear naturally: atoms are conserved
along the condensation path, molecules are not.

### Condensation curves are an input, deliberately

The heat release of a condensing sulfur stream depends on the full reactive VLE
of the allotrope mixture together with the water, H₂S, SO₂, CO₂ and nitrogen
around it. Reproducing that inside a rating engine means reimplementing a flash
package — and doing it *slightly differently* from the simulator that produced
the process guarantee is worse than not doing it at all, because the rating and
the heat balance would then disagree with no way to say which is right.

So `CondensationCurve` imports the curve point by point from ProMax, HYSYS with
Sulsim, or Symmetry, and its job is to validate, interpolate and differentiate
it. `Speciation` stays useful alongside as an independent check on an imported
curve, and for when no simulator run is available.

Validation rejects the three common export faults outright, because each one
interpolates cleanly and rates to nonsense: a file ordered cold-to-hot,
non-increasing cumulative duty (duty exported as a rate), and vapour fractions
outside [0,1]. A curve with fewer than 10 points is warned about.

From the curve it derives **dT/dh**, which feeds the Silver / Bell & Ghaly Z
factor. On the sample curve that slope falls by a factor of 2.6 from the hot end
to the cold end — which is why a condenser is rated in equal-duty zones, and why
those zones have very unequal temperature spans (64 K down to 28 K over five
zones on this curve).

The vapour heat capacity needed for Z is never defaulted: it is either exported
with the curve or supplied explicitly, because a wrong value propagates straight
into the effective coefficient.


## Alte temperature: dissociazione, trasporto stimato, radiazione

### Range coperto

| | Fino a |
|---|---|
| Cp NASA-9, tutte le 48 specie | **6000 K** (20 000 K per i gas permanenti) |
| Trasporto NASA CEA, 22 specie | 5000–15 000 K |
| Trasporto Sutherland, 8 specie | 1800 K nominali, ma degrada sopra ~1300 K |
| Senza trasporto, 18 specie | — Chung disponibile come stima |

### Dissociazione (WhbThermo.Properties.Equilibrium)

Entropia assoluta, Gibbs e costanti di equilibrio per **qualsiasi** specie del
database — non più solo per gli allotropi dello zolfo dentro XSulfur. Sopra i
1000 °C il Cp da solo non basta: serve l'entropia assoluta e l'entalpia di
formazione, che sono nei coefficienti `b` del fit NASA-9. Ogni specie li ha.

Entalpie di formazione a 298.15 K riprodotte **esattamente** (H 218.0, O 249.2,
N 472.7, OH 37.3, S 277.2, CO₂ −393.5 kJ/mol) e entropie standard entro 0.1
J/(mol·K). È il controllo più tagliente sulle costanti di integrazione: il
polinomio Cp può essere giusto mentre b₁ è sbagliato, e solo l'entalpia assoluta
lo rivela.

Reazioni pre-definite per il servizio WHB/SRU: cracking H₂S, depolimerizzazione
S₈→4S₂, cracking NH₃, water gas shift, reazione Claus, idrolisi COS,
dissociazione SO₂. Il cracking H₂S riproduce i dati pubblicati: **19.6 % a
1000 °C, 54.8 % a 1400 °C**.

Aggiunte 12 specie atomiche e radicaliche (H, O, N, S, OH, SH, SO, CH₃, HO₂,
CS, NH₂, CN): sotto i 1000 °C sono trascurabili, sopra sono ciò che la
dissociazione produce, e senza di loro nessun equilibrio si chiude.

### Trasporto stimato (Chung-Lee-Starling)

Per le specie senza dati misurati. Validato: N₂ entro 1.6 % a 300 e 1000 K,
CO₂ 0.9 %, CH₄ 1.6 %, NH₃ 1.2 %, H₂O 7.8 %. **Ogni chiamata emette un warning
che la dichiara stimata** — 5 % sugli apolari, 10–15 % sui polari, peggio sulla
conducibilità.

### Radiazione: la lacuna dichiarata

Il WSGG Smith copre **solo H₂O e CO₂**. In servizio Claus mancano SO₂ (1–3 %) e
H₂S (2–5 %), entrambi assorbitori IR sempre presenti. **Cercato: non esiste un
set di coefficienti WSGG o Leckner aperto per nessuno dei due.** La letteratura
recente estende il WSGG a CH₄, CO, fuliggine e vapori di idrocarburi, non ai
composti dello zolfo.

`Wsgg.emissivityWithCoverage` riporta esplicitamente le specie radianti presenti
nella miscela che il modello **non** contabilizza, marca il risultato come
`Estimated` e avvisa che l'emissività è **bassa di una quantità non
quantificabile**. Un'emissività silenziosamente bassa del 10–20 % è
indistinguibile da una giusta.

### Come il software segnala un dato mancante

Tre meccanismi, ciascuno per un momento diverso:

| Situazione | Meccanismo |
|---|---|
| Nessun dato, non si può calcolare | **ROP `Failure`** — `NoTransportData`, `UnknownSpecies` |
| Calcolato ma con riserva | **canale warning ROP** — Chung, fuori range, copertura radiativa |
| Il numero viaggia verso un report | **`Qualified<'T>` con `DataQuality`** |

Il terzo è la novità, e serve perché i primi due coprono il momento del
**calcolo**, non quello del **reporting**: quaranta chiamate più tardi i warning
sono stati aggregati o non letti, e un numero stimato è identico a uno misurato.

`Qualified<'T>` porta la provenienza *con* il valore attraverso ogni passaggio
aritmetico. `Qualified.combine` prende sempre la qualità **peggiore**: una
proprietà di miscela calcolata da un componente misurato e uno stimato è
stimata. È la regola onesta ed è anche l'unica che non si può aggirare.
`Qualified.caveats` restituisce l'elenco per l'appendice della relazione.


## Copertura dello schema delle proprietà

48 specie. Ogni nodo dello schema è **presente oppure porta la ragione registrata
della sua assenza** — un campo vuoto in silenzio è il difetto che un test
impedisce.

| Nodo | Copertura |
|---|---|
| ID, Name, **Formula**, MolecularWeight | 48/48 |
| Cp(T), **Cv(T)**, H(T), S(T), **HFormation**, **GFormation** | 48/48 |
| Viscosity(T), ThermalConductivity(T) | 26 misurate + 8 Sutherland + 14 via Chung stimato |
| **BinaryDiffusivity** | 48/48 volumi di Fuller |
| Tc, Pc, ω | 29/48 — le altre 19 sono radicali e allotropi, che **non hanno punto critico** |
| **VaporPressure** | 27/48 — mancano solo COS e NO₂ come lacuna reale |
| **ParticipatingGas**, RadiationParameters | 16 trasparenti, 2 coperte, 30 partecipanti **scoperte** |

### Cosa è cambiato

**Formula** è ora un campo distinto dalla chiave. Coincidono per 46 specie su 48
e divergono proprio dove conta: DME ha chiave `DME` e formula `C2H6O`, lo zolfo
atomico chiave `S1` e formula `S`.

**Proprietà critiche** da 15 a 29. Le 19 restanti sono radicali (nessuna fase
condensata) e allotropi dello zolfo (in equilibrio reattivo continuo, senza
punti critici individuali). Non sono lacune: la ragione è registrata per specie e
un test verifica che ci sia.

**Tensione di vapore**, DIPPR 101 da Perry Tabella 2-8. Ai punti di ebollizione
normali restituisce un'atmosfera entro lo **0.3 %** su quattro record
indipendenti — H₂O, C₆H₆, NH₃, CH₃OH.

**Diffusività binaria** con Fuller, volumi calcolati dalla formula per incrementi
atomici. Validata: CO₂-N₂ 0.164 cm²/s contro 0.165 misurati, H₂-N₂ 0.787 contro
0.779, CH₄-N₂ 0.218 contro 0.212.

È il nodo che serviva davvero al rating Claus. Il coefficiente di trasferimento
di massa lato gas è controllato dalla diffusione dello zolfo negli
incondensabili, e **il numero di Lewis ora si calcola invece di essere passato
come argomento** — cioè indovinato. Per S₂ in N₂ a 573 K viene 1.64, e gli
allotropi pesanti diffondono più lentamente: S₂ > S₆ > S₈, quindi la
distribuzione allotropica conta per il trasferimento di massa e non solo per il
rilascio termico.

**ParticipatingGas** è ora una proprietà della specie, non solo del modello. Tre
stati: trasparente (diatomiche omonucleari e monoatomiche), partecipante e
coperta (H₂O, CO₂), partecipante e **scoperta** — trenta specie, fra cui SO₂ e
H₂S. Per queste il messaggio dice esplicitamente che l'emissività calcolata è
**bassa**, e `uncoveredRadiators` permette a una relazione di elencare cosa
manca invece di lasciarlo implicito.
