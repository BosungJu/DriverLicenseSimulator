using System;
using System.Globalization;
using NUnit.Framework;

public class DrivingReportCsvTests
{
	private const string ExpectedHeader =
		"time_s,point_number,speed_kmh,accel_mps2,yaw_rate_dps,heading_deg,pos_x,pos_z,steering_input,throttle_input,brake_applied," +
		"engine_on,left_signal_on,right_signal_on,hazard_on,headlight_on,high_beam_on";

	[TestCase(false, "0")]
	[TestCase(true, "1")]
	public void Build_ControlInputs_ExportsSignedValuesAndBinaryBrake(bool isBraking, string expectedBrake)
	{
		var sample = new DrivingSample
		{
			TimeSeconds = 0.02f,
			PointNumber = 2,
			SpeedKmh = 4f,
			LongitudinalAccelerationMps2 = -0.5f,
			YawRateDegreesPerSecond = 3f,
			HeadingDegrees = 90f,
			PositionX = 10f,
			PositionZ = 20f,
			HasControlInput = true,
			SteeringInput = -0.25f,
			ThrottleInput = -0.5f,
			IsBraking = isBraking
		};
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			string[] lines = DrivingReportCsv.Build(new[] { sample }).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			Assert.AreEqual(ExpectedHeader, lines[0]);
			Assert.AreEqual("0.020,2,4.000,-0.500,3.000,90.00,10.000,20.000,-0.250,-0.500," + expectedBrake + ",,,,,,", lines[1]);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[Test]
	public void Build_VehicleSystemState_ExportsEachFlagInHeaderOrder()
	{
		var sample = new DrivingSample
		{
			PointNumber = 1,
			HasControlInput = true,
			HasVehicleSystemState = true,
			IsEngineOn = true,
			IsLeftSignalOn = false,
			IsRightSignalOn = true,
			IsHazardOn = false,
			IsHeadlightOn = true,
			IsHighBeamOn = false
		};

		string[] lines = DrivingReportCsv.Build(new[] { sample }).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

		Assert.AreEqual("0.000,1,0.000,0.000,0.000,0.00,0.000,0.000,0.000,0.000,0,1,0,1,0,1,0", lines[1]);
	}

	[Test]
	public void Build_UnavailableControlInputs_PreservesPhysicsAndExportsBlankInputs()
	{
		var sample = new DrivingSample { PointNumber = 1, SpeedKmh = 4f };
		string[] lines = DrivingReportCsv.Build(new[] { sample }).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		Assert.AreEqual("0.000,1,4.000,0.000,0.000,0.00,0.000,0.000,,,,,,,,,", lines[1]);
	}

	[Test]
	public void Build_EmptySamples_ExportsOnlyHeader()
	{
		Assert.AreEqual(DrivingReportCsv.Header + Environment.NewLine, DrivingReportCsv.Build(Array.Empty<DrivingSample>()));
	}
}
