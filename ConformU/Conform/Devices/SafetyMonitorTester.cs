﻿using ASCOM.Alpaca.Clients;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;
using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConformU
{

    internal class SafetyMonitorTester : DeviceTesterBaseClass
    {
        private bool m_IsSafe;
        private ISafetyMonitorV3 m_SafetyMonitor;

        // Helper variables
        internal static Utilities g_Util;
        private readonly CancellationToken cancellationToken;
        private readonly Settings settings;
        private readonly ConformLogger logger;

        // IDisposable
        private bool disposedValue = false;        // To detect redundant calls

        private enum RequiredProperty
        {
            propIsSafe
        }
        private enum PerformanceProperty
        {
            propIsSafe
        }

        #region New and Dispose

        public SafetyMonitorTester(ConformConfiguration conformConfiguration, ConformLogger logger, CancellationToken conformCancellationToken) : base(false, true, false, false, true, true, false, conformConfiguration, logger, conformCancellationToken) // Set flags for this device:  HasCanProperties, HasProperties, HasMethods, PreRunCheck, PreConnectCheck, PerformanceCheck, PostRunCheck
        {
            g_Util = new();
            //g_settings.MessageLevel = MessageLevel.Debug;

            settings = conformConfiguration.Settings;
            cancellationToken = conformCancellationToken;
            this.logger = logger;
        }

        protected override void Dispose(bool disposing)
        {
            LogDebug("Dispose", "Disposing of test device: " + disposing.ToString() + " " + disposedValue.ToString());
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (m_SafetyMonitor is not null) m_SafetyMonitor.Dispose();
                }
            }

            // Call the DeviceTesterBaseClass dispose method
            base.Dispose(disposing);
            disposedValue = true;
        }

        #endregion

        public new void CheckInitialise()
        {
            // Set the error type numbers according to the standards adopted by individual authors.
            // Unfortunately these vary between drivers so I have to allow for these here in order to give meaningful
            // messages to driver authors!
            unchecked
            {
                switch (settings.ComDevice.ProgId ?? "")
                {
                    default:
                        {
                            g_ExNotImplemented = (int)0x80040400;
                            g_ExInvalidValue1 = (int)0x80040405;
                            g_ExInvalidValue2 = (int)0x80040405;
                            g_ExInvalidValue3 = (int)0x80040405;
                            g_ExInvalidValue4 = (int)0x80040405;
                            g_ExInvalidValue5 = (int)0x80040405;
                            g_ExInvalidValue6 = (int)0x80040405;
                            g_ExNotSet1 = (int)0x80040403;
                            break;
                        }
                }
            }
        }

        public override void CreateDevice()
        {
            try
            {
                switch (settings.DeviceTechnology)
                {
                    case DeviceTechnology.Alpaca:
                        LogInfo("CreateDevice", $"Creating Alpaca device: IP address: {settings.AlpacaDevice.IpAddress}, IP Port: {settings.AlpacaDevice.IpPort}, Alpaca device number: {settings.AlpacaDevice.AlpacaDeviceNumber}");
                        m_SafetyMonitor = new AlpacaSafetyMonitor(
                                                    settings.AlpacaConfiguration.AccessServiceType,
                                                    settings.AlpacaDevice.IpAddress,
                                                    settings.AlpacaDevice.IpPort,
                                                    settings.AlpacaDevice.AlpacaDeviceNumber,
                                                    settings.AlpacaConfiguration.EstablishConnectionTimeout,
                                                    settings.AlpacaConfiguration.StandardResponseTimeout,
                                                    settings.AlpacaConfiguration.LongResponseTimeout,
                                                    Globals.CLIENT_NUMBER_DEFAULT,
                                                    settings.AlpacaConfiguration.AccessUserName,
                                                    settings.AlpacaConfiguration.AccessPassword,
                                                    settings.AlpacaConfiguration.StrictCasing,
                                                    settings.TraceAlpacaCalls ? logger : null,
                                                    Globals.USER_AGENT_PRODUCT_NAME,
                                                    Assembly.GetExecutingAssembly().GetName().Version.ToString(4),
                                                    settings.AlpacaConfiguration.TrustUserGeneratedSslCertificates);

                        LogInfo("CreateDevice", $"Alpaca device created OK");
                        break;

                    case DeviceTechnology.COM:
                        switch (settings.ComConfiguration.ComAccessMechanic)
                        {
                            case ComAccessMechanic.Native:
                                LogInfo("CreateDevice", $"Creating NATIVE COM device: {settings.ComDevice.ProgId}");
                                m_SafetyMonitor = new SafetyMonitorFacade(settings, logger);
                                break;

                            case ComAccessMechanic.DriverAccess:
                                LogInfo("CreateDevice", $"Creating DRIVERACCESS device: {settings.ComDevice.ProgId}");
                                m_SafetyMonitor = new SafetyMonitor(settings.ComDevice.ProgId);
                                break;

                            default:
                                throw new ASCOM.InvalidValueException($"CreateDevice - Unknown COM access mechanic: {settings.ComConfiguration.ComAccessMechanic}");
                        }
                        break;

                    default:
                        throw new ASCOM.InvalidValueException($"CreateDevice - Unknown technology type: {settings.DeviceTechnology}");
                }

                LogInfo("CreateDevice", "Successfully created driver");
                baseClassDevice = m_SafetyMonitor; // Assign the driver to the base class

                SetFullStatus("Create device", "Waiting for driver to stabilise", "");
                WaitFor(1000, 100);
            }
            catch (COMException exCom) when (exCom.ErrorCode == REGDB_E_CLASSNOTREG)
            {
                LogDebug("CreateDevice", $"Exception thrown: {exCom.Message}\r\n{exCom}");

                throw new Exception($"The driver is not registered as a {(Environment.Is64BitProcess ? "64bit" : "32bit")} driver");
            }
            catch (Exception ex)
            {
                LogDebug("CreateDevice", $"Exception thrown: {ex.Message}\r\n{ex}");
                throw; // Re throw exception 
            }
        }

        public override void PreConnectChecks()
        {
            // Confirm that key properties are false when not connected
            try
            {
                LogCallToDriver("IsSafe", "About to get IsSafe property");
                m_IsSafe = m_SafetyMonitor.IsSafe;
                if (!m_IsSafe)
                    LogOK("IsSafe", "Reports false before connection");
                else
                    LogIssue("IsSafe", "Reports true before connection rather than false");
            }
            catch (Exception ex)
            {
                LogIssue("IsSafe", "Cannot confirm that IsSafe is false before connection because it threw an exception: " + ex.Message);
            }
        }
        public override bool Connected
        {
            get
            {
                LogCallToDriver("ConformanceCheck", "About to get Connected");
                return m_SafetyMonitor.Connected;
            }
            set
            {
                LogCallToDriver("ConformanceCheck", "About to set Connected");
                SetTest("Connected");
                SetAction("Waiting for Connected to become 'true'");
                m_SafetyMonitor.Connected = value;
                ResetTestActionStatus();

                // Make sure that the value set is reflected in Connected GET
                bool connectedState = Connected;
                if (connectedState != value)
                {
                    throw new ASCOM.InvalidOperationException($"Connected was set to {value} but Connected Get returned {connectedState}.");
                }
            }
        }

        public override void CheckCommonMethods()
        {
            base.CheckCommonMethods(m_SafetyMonitor, DeviceTypes.SafetyMonitor);
        }

        public override void CheckProperties()
        {
            RequiredPropertiesTest(RequiredProperty.propIsSafe, "IsSafe");
        }
        public override void CheckPerformance()
        {
            SetTest("Performance");
            PerformanceTest(PerformanceProperty.propIsSafe, "IsSafe");
            SetTest("");
            SetAction("");
            SetStatus("");
        }
        public override void CheckConfiguration()
        {
            try
            {
                // Common configuration
                if (!settings.TestProperties)
                    LogConfigurationAlert("Property tests were omitted due to Conform configuration.");

                if (!settings.TestMethods)
                    LogConfigurationAlert("Method tests were omitted due to Conform configuration.");

            }
            catch (Exception ex)
            {
                LogError("CheckConfiguration", $"Exception when checking Conform configuration: {ex.Message}");
                LogDebug("CheckConfiguration", $"Exception detail:\r\n:{ex}");
            }
        }

        private void RequiredPropertiesTest(RequiredProperty p_Type, string p_Name)
        {
            try
            {
                switch (p_Type)
                {
                    case RequiredProperty.propIsSafe:
                        {
                            m_IsSafe = m_SafetyMonitor.IsSafe;
                            LogCallToDriver("IsSafe", "About to get IsSafe property");
                            LogOK(p_Name, m_IsSafe.ToString());
                            break;
                        }

                    default:
                        {
                            LogIssue(p_Name, "RequiredPropertiesTest: Unknown test type " + p_Type.ToString());
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                HandleException(p_Name, MemberType.Property, Required.Mandatory, ex, "");
            }
        }
        private void PerformanceTest(PerformanceProperty p_Type, string p_Name)
        {
            DateTime l_StartTime;
            double l_Count, l_LastElapsedTime, l_ElapsedTime, l_Rate;
            SetAction(p_Name);
            try
            {
                l_StartTime = DateTime.Now;
                l_Count = 0.0;
                l_LastElapsedTime = 0.0;
                do
                {
                    l_Count += 1.0;
                    switch (p_Type)
                    {
                        case PerformanceProperty.propIsSafe:
                            {
                                m_IsSafe = m_SafetyMonitor.IsSafe;
                                break;
                            }

                        default:
                            {
                                LogIssue(p_Name, "PerformanceTest: Unknown test type " + p_Type.ToString());
                                break;
                            }
                    }

                    l_ElapsedTime = DateTime.Now.Subtract(l_StartTime).TotalSeconds;
                    if (l_ElapsedTime > l_LastElapsedTime + 1.0)
                    {
                        SetStatus(l_Count + " transactions in " + l_ElapsedTime.ToString("0") + " seconds");
                        l_LastElapsedTime = l_ElapsedTime;
                        if (cancellationToken.IsCancellationRequested)
                            return;
                    }
                }
                while (l_ElapsedTime <= PERF_LOOP_TIME);
                l_Rate = l_Count / l_ElapsedTime;
                switch (l_Rate)
                {
                    case object _ when l_Rate > 10.0:
                        {
                            LogInfo(p_Name, "Transaction rate: " + l_Rate.ToString("0.0") + " per second");
                            break;
                        }

                    case object _ when 2.0 <= l_Rate && l_Rate <= 10.0:
                        {
                            LogOK(p_Name, "Transaction rate: " + l_Rate.ToString("0.0") + " per second");
                            break;
                        }

                    case object _ when 1.0 <= l_Rate && l_Rate <= 2.0:
                        {
                            LogInfo(p_Name, "Transaction rate: " + l_Rate.ToString("0.0") + " per second");
                            break;
                        }

                    default:
                        {
                            LogInfo(p_Name, "Transaction rate: " + l_Rate.ToString("0.0") + " per second");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                LogInfo(p_Name, "Unable to complete test: " + ex.ToString());
            }
        }
    }

}
