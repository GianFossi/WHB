namespace Whb.Core.Loads

module Loads =

    type LoadSource = ClientPds | VendorPds | Calculated | Manual

    [<CLIMutable>]
    type ThermalLoad =
        { Tag: string
          Source: LoadSource
          GasMassFlow: float
          GasInletTemperature: float
          GasInletPressure: float
          TargetOutletTemperature: float option
          Duty: float option
          Notes: string }

    [<CLIMutable>]
    type LoadSet =
        { Name: string; Loads: ThermalLoad list }
