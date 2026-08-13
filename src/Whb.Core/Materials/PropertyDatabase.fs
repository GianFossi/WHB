namespace Whb.Core.MaterialData

open Whb.Core.Components.Geometry

module PropertyDatabase =

    [<CLIMutable>]
    type FluidProperties =
        { Name: string
          Temperature: float
          Pressure: float
          Density: float
          Cp: float
          Viscosity: float
          Conductivity: float }

    [<CLIMutable>]
    type MaterialProperties =
        { Name: string
          Density: float
          Conductivity20C: float
          YoungModulus20C: float
          Yield20C: float
          Notes: string }

    let steels =
        [ { Name = "SA-192 / SA-210 A1"; Density = 7850.0; Conductivity20C = 52.0; YoungModulus20C = 207e9; Yield20C = 255e6; Notes = "Tubi caldaia al carbonio." }
          { Name = "SA-213 T11"; Density = 7850.0; Conductivity20C = 42.0; YoungModulus20C = 210e9; Yield20C = 275e6; Notes = "1.25Cr-0.5Mo per zone calde." }
          { Name = "SA-533 Gr.B Cl.2"; Density = 7850.0; Conductivity20C = 41.0; YoungModulus20C = 207e9; Yield20C = 485e6; Notes = "Lamiera recipiente a pressione." }
          { Name = "Alloy 602 CA"; Density = 7600.0; Conductivity20C = 10.5; YoungModulus20C = 217e9; Yield20C = 270e6; Notes = "Liner ad alta temperatura." } ]

    let materialRef (name: string) =
        steels
        |> List.tryFind (fun x -> x.Name.ToLowerInvariant().Contains(name.ToLowerInvariant()))
        |> Option.defaultValue steels.Head
        |> fun m -> { Name = m.Name; Density = m.Density }
