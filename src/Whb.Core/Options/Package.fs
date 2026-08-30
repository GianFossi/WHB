namespace Whb.Core

open Whb.Equipment
module Package =

    type Package = EquipmentPackage

    let totalMetrics (p: Package) =
        p.Metrics

    let fromWhbCore (source: Interop.IWhbCoreEquipmentSnapshot) =
        EquipmentPackage.ofWhbCore source


