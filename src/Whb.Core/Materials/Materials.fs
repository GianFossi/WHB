namespace Whb.Core
module Materials =
    /// <summary>
    /// Describes a metallic material with its thermal, mechanical, and corrosion properties.
    /// </summary>
    type Material =
        { Name: string
          K: float -> float
          TmaxDesign: float
          Alpha: float -> float
          E: float -> float
          Sy: float -> float
          MetalDusting: (float * float) option
          Note: string }
    /// <summary>
    /// Builds a linear thermal-conductivity model from a reference value and a slope.
    /// </summary>
    /// <param name="k0">Thermal conductivity at 0 °C in W/(m·K).</param>
    /// <param name="slope">Temperature slope in W/(m·K²).</param>
    /// <returns>A conductivity function of temperature.</returns>
    let private lin (k0: float) (slope: float) = fun (t: float) -> k0 + slope * t
    /// <summary>
    /// Carbon steel material profile typically used for low-temperature sections and shell components.
    /// </summary>
    let carbonSteel =
        { Name = "SA-192 / SA-210 A1 (acciaio al carbonio)"
          K = lin 52.0 -0.028
          E = (fun t -> 2.07e+11 - 5.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.55e+08 - 220000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 450.0
          MetalDusting = Some(400.0, 800.0)
          Note = "Limite pratico 450 °C (creep + grafitizzazione oltre 425 °C)." }
    /// <summary>
    /// 0.5Mo steel used for moderate-temperature heat-transfer sections.
    /// </summary>
    let t1Mo =
        { Name = "SA-209 T1 (0.5Mo)"
          K = lin 49.0 -0.024
          E = (fun t -> 2.08e+11 - 5.6e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.6e+08 - 210000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 480.0
          MetalDusting = Some(400.0, 800.0)
          Note = "Grafitizzazione possibile oltre 450 °C in esercizio prolungato." }
    /// <summary>
    /// 1.25Cr-0.5Mo alloy profile used in hot gas-side sections.
    /// </summary>
    let t11 =
        { Name = "SA-213 T11 (1.25Cr-0.5Mo)"
          K = lin 42.0 -0.014
          E = (fun t -> 2.1e+11 - 5.8e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.75e+08 - 200000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.16e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 550.0
          MetalDusting = Some(430.0, 820.0)
          Note = "Buon compromesso per zone calde di WHB syngas." }
    /// <summary>
    /// 2.25Cr-1Mo steel profile for hot, highly stressed service.
    /// </summary>
    let t22 =
        { Name = "SA-213 T22 (2.25Cr-1Mo)"
          K = lin 38.0 -0.010
          E = (fun t -> 2.1e+11 - 5.7e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.75e+08 - 190000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.14e-05 + 3.2e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 580.0
          MetalDusting = Some(430.0, 850.0)
          Note = "Standard per tubi caldi di WHB reforming." }
    /// <summary>
    /// Austenitic stainless steel profile for corrosive hot-gas service.
    /// </summary>
    let ss321h =
        { Name = "SA-213 TP321H (austenitico)"
          K = fun t -> 14.5 + 0.0155 * t
          E = (fun t -> 1.95e+11 - 7e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (2.05e+08 - 120000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.6e-05 + 4.5e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 700.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Suscettibile a SCC da cloruri lato acqua: da evitare a contatto con BFW." }
    /// <summary>
    /// Alloy 800 profile for ferrule and high-temperature insert applications.
    /// </summary>
    let alloy800 =
        { Name = "Alloy 800H/800HT"
          K = fun t -> 11.5 + 0.0165 * t
          E = (fun t -> 1.96e+11 - 6.2e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (1.7e+08 - 60000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.47e-05 + 4.3e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 800.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Tipico per ferrule/inserti e boccole in zona ingresso gas." }
    /// <summary>
    /// Alloy 601 profile for high-temperature liner and oxidation-resistant applications.
    /// </summary>
    let alloy601 =
        { Name = "Alloy 601 / 602 CA (liner by-pass)"
          K = fun t -> 11.3 + 0.0163 * t
          Alpha = (fun t -> 15.3e-6 + 4.0e-9 * (max 0.0 (t - 20.0)))
          E = (fun t -> 207e9 - 0.075e9 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (240e6 - 0.11e6 * (max 0.0 (t - 20.0))))
          TmaxDesign = 1100.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Alto Cr-Al, resistente a ossidazione e carburizzazione ad alta temperatura." }
    /// <summary>
    /// SA-533 Grade B profile for pressure-vessel shell constructions and boned plate service.
    /// </summary>
    let sa533b2 =
        { Name = "SA-533 Gr.B Cl.2 (Mn-Mo-Ni bonificato)"
          K = lin 41.0 -0.017
          E = (fun t -> 2.07e+11 - 5.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 200e6 (4.85e+08 - 380000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.18e-05 + 4.0e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 400.0
          MetalDusting = None
          Note = "Lamiera per recipienti a pressione, bonificata. Limite pratico ASME VIII ~371 °C." }
    /// <summary>
    /// Alloy 602 profile for very high-temperature liner service.
    /// </summary>
    let alloy602 =
        { Name = "SB-168 UNS N06025 (Alloy 602 CA)"
          K = lin 10.5 0.0160
          E = (fun t -> 2.17e+11 - 7.5e+07 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 60e6 (2.70e+08 - 105000.0 * (max 0.0 (t - 20.0))))
          Alpha = (fun t -> 1.42e-05 + 4.0e-09 * (max 0.0 (t - 20.0)))
          TmaxDesign = 1200.0
          MetalDusting = Some(450.0, 900.0)
          Note = "Lega per liner ad altissima temperatura: resistenza alla carburazione data dal 2.2 % di Al." }
    /// <summary>
    /// SA-516 Grade 70 profile for shell and non-process-exposed sections.
    /// </summary>
    let sa516 =
        { Name = "SA-516 Gr.70 (lamiera mantello)"
          K = lin 52.0 -0.028
          Alpha = (fun t -> 1.15e-05 + 4e-09 * (max 0.0 (t - 20.0)))
          E = (fun t -> 202e9 - 0.052e9 * (max 0.0 (t - 20.0)))
          Sy = (fun t -> max 40e6 (260e6 - 0.23e6 * (max 0.0 (t - 20.0))))
          TmaxDesign = 425.0
          MetalDusting = None
          Note = "Materiale di mantello tipico; non esposto al gas di processo." }
    /// <summary>
    /// Returns the complete catalog of supported alloy profiles.
    /// </summary>
    /// <returns>The full list of material definitions.</returns>
    let all = [ carbonSteel; t1Mo; t11; t22; ss321h; alloy800; alloy601; alloy602; sa516; sa533b2 ]
    /// <summary>
    /// Finds the closest material record by a name fragment.
    /// </summary>
    /// <param name="n">A material name fragment or abbreviation.</param>
    /// <returns>The matching material or the default carbon steel profile.</returns>
    let byName (n: string) =
        all
        |> List.tryFind (fun m -> m.Name.ToLowerInvariant().Contains(n.ToLowerInvariant()))
        |> Option.defaultValue carbonSteel
    /// <summary>
    /// Calculates the thermal elongation of a material between room temperature and the target condition.
    /// </summary>
    /// <param name="m">The material definition.</param>
    /// <param name="tRoom">Reference temperature in °C.</param>
    /// <param name="l">Original length in meters.</param>
    /// <param name="t">Target temperature in °C.</param>
    /// <returns>The thermal elongation in meters.</returns>
    let elongation (m: Material) (tRoom: float) (l: float) (t: float) =
        m.Alpha t * (t - tRoom) * l
    /// <summary>
    /// Provides thermal conductivity models for common refractory layers.
    /// </summary>
    module Refractory =
        /// <summary>
        /// Light castable refractory conductivity model.
        /// </summary>
        /// <param name="t">Temperature in °C.</param>
        /// <returns>Conductivity in W/(m·K).</returns>
        let castableLight (t: float) = 0.35 + 0.00025 * t
        /// <summary>
        /// Ceramic fiber conductivity model.
        /// </summary>
        /// <param name="t">Temperature in °C.</param>
        /// <returns>Conductivity in W/(m·K).</returns>
        let ceramicFibre (t: float) = 0.12 + 0.00035 * t
        /// <summary>
        /// Dense castable refractory conductivity model.
        /// </summary>
        /// <param name="t">Temperature in °C.</param>
        /// <returns>Conductivity in W/(m·K).</returns>
        let castableDense (t: float) = 1.2 + 0.0003 * t
        /// <summary>
        /// Saffil paper conductivity model.
        /// </summary>
        /// <param name="t">Temperature in °C.</param>
        /// <returns>Conductivity in W/(m·K).</returns>
        let saffilPaper (t: float) = 0.07 + 0.00015 * t


