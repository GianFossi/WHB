namespace Whb.Core

open Constants
open Types

/// <summary>
/// Provides defaults functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Defaults =

    /// <summary>
    /// Calculates or returns referencecomposition for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let referenceComposition : GasProps.Composition =
        [ GasProps.H2,  0.3707
          GasProps.N2,  0.1577
          GasProps.CO,  0.0863
          GasProps.CO2, 0.0546
          GasProps.CH4, 0.0027
          GasProps.Ar,  0.0020
          GasProps.H2O, 0.3260 ]

    /// <summary>
    /// Calculates or returns referencecase for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let referenceCase : DesignCase =
        { Name = "WHB reformer secondario - 848 tubi OD38.1x3.05 L12998 - vapore 117.84 bar (portate +10%)"
          Tube =
            { Di = 0.0320                 // 38.1 - 2 x 3.05
              Do = 0.0381
              Length = 12.998
              NTubes = 848
              Pitch = 0.0508
              Staggered = true            // triangolare 60°
              ShellId = 2.025
              Otl = 1.71111
              Itl = 0.571
              BaffleOd = 2.015          // diaframmi quasi a filo mantello (ID 2025)
              Roughness = 4.5e-5 }
          Ferrule =
            { Enabled = true
              Lengths = [ (1.0, 0.200) ]
              Bore = 0.0267
              SleeveOd = 0.0300
              SleeveK = Materials.alloy800.K
              InsulK = Materials.Refractory.saffilPaper }
          Gas =
            { Composition = referenceComposition
              MassFlow = 279566.0 * 1.1 / 3600.0     // 85.42 kg/s
              TIn = cToK 967.5
              PIn = barToPa 34.74
              Z = 1.0
              FoulingIn = 5.0e-4
              EpsWall = 0.85
              Radiation = true
              EntranceC = 1.4
              Correlation = GasSide.Gnielinski
              ShiftMode = Shift.Frozen
              MixingRule = GasProps.Wilke
              RealGas = true }
          Water =
            { DrumPressure = barToPa 117.84
              FoulingOut = 1.5e-4
              RoughnessUm = 1.0
              BundleFactor = 1.5
              Correlation = WaterSide.Mostinski
              Csf = 0.013
              TFeed = cToK 250.0 }
          Loop =
            { DzDrumWhb = 6.0
              DrumLevelOffset = 0.150
              Downcomers =
                [ Piping.line "DC1" "18\" Sch.120" 387.2 1 [ 0.250; 2.623; 2.376 ]
                    [ Piping.elbow 90.0 1.5 1; Piping.elbow 30.0 1.5 2 ] 0.0 0.550 150.0
                    "vicino alla piastra tubiera calda"
                  Piping.line "DC2" "18\" Sch.120" 387.2 1 [ 0.250; 2.623; 2.376 ]
                    [ Piping.elbow 90.0 1.5 1; Piping.elbow 30.0 1.5 2 ] 0.0 0.550 210.0
                    "vicino alla piastra tubiera calda"
                  Piping.line "DC3" "16\" Sch.120" 344.6 1 [ 0.250; 3.040; 2.621 ]
                    [ Piping.elbow 60.0 1.5 1; Piping.elbow 90.0 1.5 1; Piping.elbow 30.0 1.5 1 ] 0.0 1.621 150.0 ""
                  Piping.line "DC4" "16\" Sch.120" 344.6 1 [ 0.250; 3.040; 2.621 ]
                    [ Piping.elbow 60.0 1.5 1; Piping.elbow 90.0 1.5 1; Piping.elbow 30.0 1.5 1 ] 0.0 1.621 210.0 ""
                  Piping.line "DC5" "16\" Sch.120" 344.6 1 [ 0.500; 2.873; 1.159; 1.377 ]
                    [ Piping.elbow 90.0 1.5 2; Piping.elbow 30.0 1.5 3 ] 0.0 2.741 180.0 ""
                  Piping.line "DC6" "16\" Sch.120" 344.6 1 [ 0.500; 2.873; 1.159; 1.377 ]
                    [ Piping.elbow 90.0 1.5 2; Piping.elbow 30.0 1.5 3 ] 0.0 3.916 180.0 ""
                  Piping.line "DC7" "16\" Sch.120" 344.6 1 [ 0.500; 2.873; 1.159; 1.377 ]
                    [ Piping.elbow 90.0 1.5 2; Piping.elbow 30.0 1.5 3 ] 0.0 5.541 180.0 ""
                  Piping.line "DC8" "16\" Sch.120" 344.6 1 [ 0.500; 2.873; 1.159; 1.377 ]
                    [ Piping.elbow 90.0 1.5 2; Piping.elbow 30.0 1.5 3 ] 0.0 8.391 180.0 ""
                  Piping.blind
                    (Piping.line "DC9" "4\" Sch.120" 92.1 1 [ 0.500; 3.000; 1.500 ]
                       [ Piping.elbow 90.0 1.5 2 ] 0.0 11.516 180.0 "estremita' fredda")
                    "NON COLLEGATO (flangia cieca)" ]
              Risers =
                [ Piping.line "R1" "24\" Sch.120" 518.0 1 [ 2.700 ] [] 0.0 1.300 0.0 ""
                  Piping.line "R2" "24\" Sch.120" 518.0 1 [ 2.700 ] [] 0.0 4.550 0.0 ""
                  Piping.line "R3" "24\" Sch.120" 518.0 1 [ 2.700 ] [] 0.0 7.800 0.0 ""
                  Piping.line "R4" "24\" Sch.120" 518.0 1 [ 2.700 ] [] 0.0 11.050 0.0 ""
                  Piping.blind
                    (Piping.line "R5" "6\" Sch.120" 139.7 1 [ 2.700 ] [] 0.0 12.550 0.0 "estremita' fredda")
                    "NON COLLEGATO (flangia cieca)"
                  Piping.blind
                    (Piping.line "R0A" "(da confermare)" 518.0 1 [ 2.700 ] [] 0.0 0.30 0.0
                       "estremita' calda")
                    "NON IMPLEMENTATO"
                  Piping.blind
                    (Piping.line "R0B" "(da confermare)" 518.0 1 [ 2.700 ] [] 0.0 0.30 0.0
                       "estremita' calda")
                    "NON IMPLEMENTATO" ]
              DrumInternalsDp = 5000.0
              Drum =
                { Enabled = true
                  ShellId = 3.000
                  Length = 13.000
                  NormalLevel = 1.650
                  ConveyorCount = 4
                  ConvDuctArea = 0.570 * 0.400
                  ConvLength = 2.30
                  ConvHydDia = 4.0 * (0.570 * 0.400) / (2.0 * (0.570 + 0.400))
                  ConvBendAngle = 150.0
                  ConvBendROverD = 3.0
                  ConvOutletArea = 0.700 * 0.500
                  ConvOutletAboveLevel = true
                  ConvExtraK = 1.0
                  DemisterArea = 1.60 * 13.0
                  DemisterK = 2.0
                  ChimneyCount = 8
                  ChimneyId = 0.2027          // 8" Sch.80
                  ChimneyK = 2.5
                  ManifoldId = 0.4778         // 20" Sch.80
                  OutletId = 0.4286           // 18" Sch.80
                  ExternalSteam = 15.573e6 * 1.1 / 1.213e6
                  VendorDpCirculation = None }
              VoidModel = TwoPhase.ZuberFindlay
              FrictionModel = TwoPhase.Friedel }
          Material = Materials.t11
          FerruleMaterial = Materials.alloy800
          NZ = 90
          NY = 12
          AxialRefine = 10.0
          RiserNozzleCount = 0
          DowncomerNozzleCount = 0
          TargetDowncomerVelocity = 2.0
          MaxRhoV2Riser = 6000.0
          MaxRhoV2Downcomer = 3000.0
          ShellThickness = 0.058
          ShellMaterial = Materials.sa533b2
          TubeLayout = Vibration.RotatedTriangular60
          VibrationDamping = 0.03
          TubesheetJoint = Vibration.FullPenetrationWeld
          UnsupportedSpan = 1.290
          BaffleSpans =
            [ 1.290; 0.935; 1.100; 1.153
              1.065; 1.065; 1.065; 1.065; 1.065; 1.065
              0.930; 0.968 ]
          BaffleThickness = 0.020
          AssemblyTemperature = cToK 20.0
          ShellInsulationU = 0.6
          Bypass =
            { Enabled = true
              Fraction = None                    // calcolata per centrare 355 °C
              TargetMixOut = cToK 355.0
              LinerId = 0.275
              LinerOd = 0.281
              LinerMaterial = Materials.alloy602
              InsulOd = 0.300
              InsulK = Materials.Refractory.saffilPaper
              PipeOd = 0.350
              PipeMaterial = Materials.sa533b2
              FoulingIn = 5.0e-4
              ExtraK = 1.5
              ValveAtOutlet = true
              ValveOpenDeg = None
              MinOpenDeg = 15.0
              MaxOpenDeg = 70.0
              TMixMin = cToK 350.0
              TMixMax = cToK 360.0
              MinPurgeVel = 1.5
              MaxRhoV2Valve = 40000.0 }
          AllowInternalRecirculation = false
          BypassOpenFraction = 0.10 }
