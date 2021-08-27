﻿using System;

namespace ConformU
{
    internal static class Globals
    {

        #region Global constants

        internal const string TECHNOLOGY_ALPACA = "Alpaca";
        internal const string TECHNOLOGY_COM = "COM";

        internal const string ASCOM_PROFILE_KEY = @"SOFTWARE\ASCOM";

        internal const string NO_DEVICE_SELECTED = "No device selected"; // Text indicating that no device has been selected

        internal const string COMMAND_OPTION_SETTINGS = "ConformSettings";
        internal const string COMMAND_OPTION_LOGFILENAME = "ConformLogFileName";
        internal const string COMMAND_OPTION_LOGFILEPATH = "ConformLogFilePath";
        internal const string COMMAND_OPTION_SHOW_DISCOVERY = "ShowDiscovery";

        internal const int TEST_NAME_WIDTH = 35; // Width allowed for test names in screen display and log files

        #endregion

        #region Global Variables

        // Variables shared between the test manager and device testers        
        internal static int g_CountError; // Internal Conform errors
        internal static int g_CountIssue; // Deviations from the ASCOM specification

        #endregion

    }

    #region Enums

    public enum ComAccessMechanic
    {
        Native = 0,
        DriverAccess = 1
    }

    public enum DeviceTechnology
    {
        NotSelected = 0,
        Alpaca = 1,
        COM = 2
    }

    public enum DeviceType
    {
        NoDeviceType = 0,
        Telescope = 1,
        Camera = 2,
        Dome = 3,
        FilterWheel = 4,
        Focuser = 5,
        ObservingConditions = 6,
        Rotator = 7,
        Switch = 8,
        SafetyMonitor = 9,
        Video = 10,
        CoverCalibrator = 11
    }

    public enum MessageLevel
    {
        Debug = 0,
        Comment = 1,
        Info = 2,
        OK = 3,
        Issue = 4,
        Error = 5,
        TestAndMessage = 6,
        TestOnly = 7
    }

    // Must be valid service types because they are used as values in Alpaca access code i.e. ServiceType.http.ToString()
    public enum ServiceType
    {
        Http = 0,
        Https = 1
    }

    #endregion

}