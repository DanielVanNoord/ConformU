﻿// Ignore Spelling: Cts Obs XX

using ASCOM.Alpaca.Clients;
using ASCOM.Common;
using ASCOM.Common.Alpaca;
using ASCOM.Common.DeviceInterfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConformU
{
    public class AlpacaProtocolTestManager : IDisposable
    {
        // Test values for ClientID and ClientTransactionID
        const int TEST_CLIENT_ID = 123456;
        const int TEST_TRANSACTION_ID = 67890;
        const string BAD_PARAMETER_VALUE = "asduio6fghZZ";

        private readonly CancellationToken applicationCancellationToken;
        private bool disposedValue;
        private readonly ConformLogger TL;
        private readonly Settings settings;
        readonly internal CancellationTokenSource applicationCancellationTokenSource;

        HttpClient httpClient;
        readonly List<string> issueMessages;
        readonly List<string> informationMessages;
        readonly List<string> errorMessages;

        #region New and Dispose

        public AlpacaProtocolTestManager(ConformConfiguration conformConfiguration, ConformLogger logger, CancellationTokenSource conformCancellationTokenSource, CancellationToken conformCancellationToken)
        {
            TL = logger;
            applicationCancellationToken = conformCancellationToken;
            applicationCancellationTokenSource = conformCancellationTokenSource;
            settings = conformConfiguration.Settings;
            issueMessages = new List<string>();
            informationMessages = new List<string>();
            errorMessages = new List<string>();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                httpClient.Dispose();
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put clean-up code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Pre-formed status lists

        // HTTP statuses used to assess the outcome of protocol tests
        readonly List<HttpStatusCode> HttpStatusCodeAny = new() { };
        readonly List<HttpStatusCode> HttpStatusCode200 = new() { HttpStatusCode.OK };
        readonly List<HttpStatusCode> HttpStatusCode400 = new() { HttpStatusCode.BadRequest };
        readonly List<HttpStatusCode> HttpStatusCode4XX = new() { HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.PaymentRequired, HttpStatusCode.Forbidden,
                                                     HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotAcceptable, HttpStatusCode.ProxyAuthenticationRequired, HttpStatusCode.Conflict, HttpStatusCode.Gone };

        #endregion

        #region Pre-formed parameter lists that can be sent to clients

        // ClientID and ClientTransactionID OK
        internal List<CheckProtocolParameter> ParamsOk = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };

        // ClientID and ClientTransactionID OK but with additional spurious parameter
        internal List<CheckProtocolParameter> ParamsOkPlusExtraParameter = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("ExtraParameter", "ExtraValue") };

        // ClientID and ClientTransactionID parameter names lower case
        internal List<CheckProtocolParameter> ParamClientIDLowerCase = new() { new CheckProtocolParameter("clientid", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };
        internal List<CheckProtocolParameter> ParamTransactionIdLowerCase = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("clienttransactionid", TEST_TRANSACTION_ID.ToString()) };

        // ClientID and ClientTransactionID values empty
        internal List<CheckProtocolParameter> ParamClientIDEmpty = new() { new CheckProtocolParameter("ClientID", ""), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };
        internal List<CheckProtocolParameter> ParamTransactionIdEmpty = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", "") };

        // ClientID and ClientTransactionID values white space
        internal List<CheckProtocolParameter> ParamClientIDWhiteSpace = new() { new CheckProtocolParameter("ClientID", "     "), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };
        internal List<CheckProtocolParameter> ParamTransactionIdWhiteSpace = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", "     ") };

        // ClientID and ClientTransactionID values negative number
        internal List<CheckProtocolParameter> ParamClientIDNegative = new() { new CheckProtocolParameter("ClientID", "-12345"), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };
        internal List<CheckProtocolParameter> ParamTransactionIdNegative = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", "-67890") };

        // ClientID and ClientTransactionID values non-numeric string
        internal List<CheckProtocolParameter> ParamClientIDString = new() { new CheckProtocolParameter("ClientID", "asdasd"), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()) };
        internal List<CheckProtocolParameter> ParamTransactionIdString = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", "qweqwe") };

        // Set Connected True and False
        internal List<CheckProtocolParameter> ParamConnectedTrue = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Connected", "True") };
        internal List<CheckProtocolParameter> ParamConnectedFalse = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Connected", "False") };

        // Set Telescope.Tracking True and False
        internal List<CheckProtocolParameter> ParamTrackingTrue = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Tracking", "True") };
        internal List<CheckProtocolParameter> ParamTrackingFalse = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Tracking", "False") };

        #endregion

        internal enum TestOutcome
        {
            OK,
            Info,
            Issue,
            Error
        }

        #region Alpaca protocol test management

        public async Task<int> TestAlpacaProtocol()
        {
            int returnCode = -99999;

            try
            {
                TL.LogMessage("TestAlpacaProtocol", $"Task started");

                string clientHostAddress = $"{settings.AlpacaDevice.ServiceType.ToString().ToLowerInvariant()}://{settings.AlpacaDevice.IpAddress}:{settings.AlpacaDevice.IpPort}";

                LogText("", $"Connecting to device: {settings.AlpacaDevice.IpAddress}:{settings.AlpacaDevice.IpPort} through URL: {clientHostAddress}");
                LogBlankLine();
                // Remove any old client, if present
                httpClient?.Dispose();

                // Convert from the Alpaca decompression enum to the HttpClient decompression enum
                DecompressionMethods decompressionMethods;
                switch (settings.AlpacaConfiguration.ImageArrayCompression)
                {
                    case ImageArrayCompression.None:
                        decompressionMethods = DecompressionMethods.None;
                        break;

                    case ImageArrayCompression.GZip:
                        decompressionMethods = DecompressionMethods.GZip;
                        break;

                    case ImageArrayCompression.Deflate:
                        decompressionMethods = DecompressionMethods.Deflate;
                        break;

                    case ImageArrayCompression.GZipOrDeflate:
                        decompressionMethods = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                        break;

                    default:
                        throw new ASCOM.InvalidValueException($"Invalid image array compression value: {settings.AlpacaConfiguration.ImageArrayCompression}");
                }

                // Create a new http handler to control authentication and automatic decompression
                HttpClientHandler httpClientHandler = new()
                {
                    PreAuthenticate = true,
                    AutomaticDecompression = decompressionMethods
                };

                // Create a new client pointing at the alpaca device
                httpClient = new HttpClient(httpClientHandler);

                // Add a basic authenticator if the user name is not null
                if (!string.IsNullOrEmpty(settings.AlpacaConfiguration.AccessUserName))
                {
                    byte[] authenticationBytes;
                    // Deal with null passwords
                    if (string.IsNullOrEmpty(settings.AlpacaConfiguration.AccessPassword)) // Handle the special case of a null string password
                    {
                        // Create authenticator bytes configured with the user name and empty password
                        authenticationBytes = Encoding.ASCII.GetBytes($"{settings.AlpacaConfiguration.AccessUserName}:");
                    }
                    else // Handle the normal case of a non-empty string username and password
                    {
                        // Create authenticator bytes configured with the user name and provided password
                        authenticationBytes = Encoding.ASCII.GetBytes($"{settings.AlpacaConfiguration.AccessUserName}:{settings.AlpacaConfiguration.AccessPassword}");
                    }

                    // Set the authentication header for all requests
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authenticationBytes));
                }

                // Set the base URI for the device
                httpClient.BaseAddress = new Uri(clientHostAddress);

                string userProductName = Globals.USER_AGENT_PRODUCT_NAME;
                string productVersion = Update.ConformuVersion;

                // Add default headers for JSON
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(AlpacaConstants.APPLICATION_JSON_MIME_TYPE));
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(userProductName, productVersion));
                httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
                httpClient.DefaultRequestHeaders.ConnectionClose = false;

                // Run the protocol test
                try
                {
                    // Set Connected true to start the test
                    await CallApi("True", "Connected", HttpMethod.Put, ParamConnectedTrue, HttpStatusCode200);
                    LogBlankLine();
                    if (applicationCancellationToken.IsCancellationRequested) goto EndOfTest; // Exit if user has pushed the STOP button

                    // Test common members
                    await TestCommon();
                    if (applicationCancellationToken.IsCancellationRequested) goto EndOfTest; // Exit if user has pushed the STOP button

                    // Test device specific members
                    switch (settings.DeviceType)
                    {
                        case DeviceTypes.Camera:
                            await TestCamera();
                            break;

                        case DeviceTypes.CoverCalibrator:
                            await TestCoverCalibrator();
                            break;

                        case DeviceTypes.Dome:
                            await TestDome();
                            break;

                        case DeviceTypes.FilterWheel:
                            await TestFilterWheel();
                            break;

                        case DeviceTypes.Focuser:
                            await TestFocuser();
                            break;

                        case DeviceTypes.ObservingConditions:
                            await TestObservingConditions();
                            break;

                        case DeviceTypes.Rotator:
                            await TestRotator();
                            break;

                        case DeviceTypes.SafetyMonitor:
                            await TestSafetyMonitor();
                            break;

                        case DeviceTypes.Switch:
                            await TestSwitch();
                            break;

                        case DeviceTypes.Telescope:
                            await TestTelescope();
                            break;

                        default:
                            throw new Exception($"Unknown device type: {settings.DeviceType}");
                    }

                EndOfTest:
                    // Finally set Connected false
                    LogBlankLine();
                    await CallApi("False", "Connected", HttpMethod.Put, ParamConnectedFalse, HttpStatusCode200, true);
                }
                catch (Exception ex)
                {
                    LogError("", ex.ToString(), null);
                }

                // Summarise the protocol test outcome
                LogBlankLine();

                // Summarise results depending on whether the test was cancelled or ran to completion
                if (applicationCancellationToken.IsCancellationRequested) // Cancellation requested
                {
                    string message = $"The Alpaca protocol checks were interrupted before completion.";
                    SetStatus(message);
                    LogLine(message);
                }
                else // Tests ran to completion
                {
                    if ((informationMessages.Count == 0) & (issueMessages.Count == 0) & (errorMessages.Count == 0))
                    {
                        SetStatus($"Congratulations, there were no errors, issues or information messages!");
                        LogLine($"Congratulations there were no errors, issues or information alerts - Your device passes ASCOM Alpaca protocol validation!!");
                    }
                    else
                    {
                        string message = $"Found {errorMessages.Count} error{(errorMessages.Count == 1 ? "" : "s")}, {issueMessages.Count} issue{(issueMessages.Count == 1 ? "" : "s")} and {informationMessages.Count} information message{(informationMessages.Count == 1 ? "" : "s")}.";
                        SetStatus(message);
                        LogLine(message);
                    }
                }

                // List any diagnostic ERROR messages
                if (errorMessages.Count > 0)
                {
                    LogBlankLine();
                    LogLine($"Error Summary");
                    foreach (string message in errorMessages)
                    {
                        LogLine(message);
                    }
                }

                // List any diagnostic ISSUE messages
                if (issueMessages.Count > 0)
                {
                    LogBlankLine();
                    LogLine($"Issue Summary");
                    foreach (string message in issueMessages)
                    {
                        LogLine(message);
                    }
                }

                // List any diagnostic INFORMATION messages
                if (informationMessages.Count > 0)
                {
                    LogBlankLine();
                    LogLine($"Information Message Summary");
                    foreach (string message in informationMessages)
                    {
                        LogLine(message);
                    }
                }

                // Set the return code to the number of errors + issues
                returnCode = errorMessages.Count + issueMessages.Count;
            }
            catch (Exception ex)
            {
                LogMessage("TestAlpacaProtocol", TestOutcome.Error, $"Exception: {ex}");
            }

            return returnCode;
        }

        #endregion

        #region Common method tests

        private async Task TestCommon()
        {
            List<CheckProtocolParameter> ParamConnectedEmpty = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Connected", "") };
            List<CheckProtocolParameter> ParamConnectedNumeric = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Connected", "123456") };
            List<CheckProtocolParameter> ParamConnectedString = new() { new CheckProtocolParameter("ClientID", TEST_CLIENT_ID.ToString()), new CheckProtocolParameter("ClientTransactionID", TEST_TRANSACTION_ID.ToString()), new CheckProtocolParameter("Connected", "asdqwe") };

            // Test primary URL structure: /api/v1/ if configured to do so
            if (settings.AlpacaConfiguration.ProtocolTestPrimaryUrlStructure)
            {
                await SendToDevice("GET Description", $"Bad Alpaca URL base element (api = apx)\"", $"/apx/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await SendToDevice("GET Description", $"Bad Alpaca URL version element (no v)", $"/api/1/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await SendToDevice("GET Description", $"Bad Alpaca URL version element (no number)", $"/api/v/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await SendToDevice("GET Description", $"Bad Alpaca URL version element (capital V)", $"/api/V1/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await SendToDevice("GET Description", $"Bad Alpaca URL version element (v2)", $"/api/v2/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button

                // Test bad POST HTTP methods
                await CallApi("True", "Connected", HttpMethod.Post, ParamConnectedTrue, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await CallApi("Value empty", "Connected", HttpMethod.Post, ParamConnectedEmpty, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await CallApi("Number", "Connected", HttpMethod.Post, ParamConnectedNumeric, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button

                // Test bad DELETE HTTP methods
                await CallApi("True", "Connected", HttpMethod.Delete, ParamConnectedTrue, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await CallApi("Empty", "Connected", HttpMethod.Delete, ParamConnectedEmpty, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
                await CallApi("Number", "Connected", HttpMethod.Delete, ParamConnectedNumeric, HttpStatusCodeAny);
                if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            }

            // Test remaining Alpaca URL structure /devicetype/devicenumber and accept any 4XX status as a correct rejection
            await SendToDevice("GET Description", $"Bad Alpaca URL device type (capitalised {settings.DeviceType.ToString().ToUpper()})", $"/api/v1/{settings.DeviceType.ToString().ToUpper()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/description", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await SendToDevice("GET Description", $"Bad Alpaca URL device type (baddevicetype)", $"/api/v1/baddevicetype/0/description", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await SendToDevice("GET Description", $"Bad Alpaca URL device number (-1)", $"/api/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/-1/description", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await SendToDevice("GET Description", $"Bad Alpaca URL device number (99999)", $"/api/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/99999/description", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await SendToDevice("GET Description", $"Bad Alpaca URL device number (A)", $"/api/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/A/description", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await SendToDevice("GET Description", $"Bad Alpaca URL method name (descrip)", $"/api/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/descrip", HttpMethod.Get, ParamsOk, HttpStatusCode4XX);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button

            // Test GET Connected
            await GetNoParameters("Connected");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button

            // Test bad PUT Connected values
            await CallApi("Bad parameter value - Empty string", "Connected", HttpMethod.Put, ParamConnectedEmpty, HttpStatusCode400, acceptInvalidValueError: true);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await CallApi("Bad parameter value - Number", "Connected", HttpMethod.Put, ParamConnectedNumeric, HttpStatusCode400, acceptInvalidValueError: true);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await CallApi("Bad parameter value - Meaningless string", "Connected", HttpMethod.Put, ParamConnectedString, HttpStatusCode400, acceptInvalidValueError: true);
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button

            // Test GET remaining common members
            await GetNoParameters("Description");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await GetNoParameters("DriverInfo");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await GetNoParameters("DriverVersion");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await GetNoParameters("InterfaceVersion");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await GetNoParameters("Name");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
            await GetNoParameters("SupportedActions");
            if (applicationCancellationToken.IsCancellationRequested) return; // Exit if user has pushed the STOP button
        }

        #endregion

        #region Device specific tests

        private async Task TestCamera()
        {
            string parameter1 = "";

            using (AlpacaCamera camera = AlpacaClient.GetDevice<AlpacaCamera>(settings.AlpacaDevice))
            {
                // Connect to the selected camera to ensure we get correct values
                try { camera.Connected = true; } catch { }

                // Test properties that don't require an image to have been taken
                await GetNoParameters("BayerOffsetX");
                await GetNoParameters("BayerOffsetY");
                await GetNoParameters("BinX");
                try { parameter1 = camera.BinX.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("BinX", "BinX", parameter1, null);
                await GetNoParameters("BinY");
                try { parameter1 = camera.BinY.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("BinY", "BinY", parameter1, null);
                await GetNoParameters("CameraState");
                await GetNoParameters("CameraXSize");
                await GetNoParameters("CameraYSize");
                await GetNoParameters("CanAbortExposure");
                await GetNoParameters("CanAsymmetricBin");
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await GetNoParameters("CanFastReadout");
                await GetNoParameters("CanGetCoolerPower");
                await GetNoParameters("CanPulseGuide");
                await GetNoParameters("CanSetCCDTemperature");
                await GetNoParameters("CanStopExposure");
                await GetNoParameters("CCDTemperature");
                await GetNoParameters("CoolerOn");
                try { parameter1 = camera.CoolerOn.ToString(); } catch (Exception) { parameter1 = "False"; }
                await PutOneParameter("CoolerOn", "CoolerOn", parameter1, null);
                await GetNoParameters("CoolerPower");
                await GetNoParameters("ElectronsPerADU");
                await GetNoParameters("ExposureMax");
                await GetNoParameters("ExposureMin");
                await GetNoParameters("ExposureResolution");
                await GetNoParameters("FastReadout");
                try { parameter1 = camera.FastReadout.ToString(); } catch (Exception) { parameter1 = "False"; }
                await PutOneParameter("FastReadout", "FastReadout", parameter1, null);
                await GetNoParameters("FullWellCapacity");
                await GetNoParameters("Gain");
                try { parameter1 = camera.Gain.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Gain", "Gain", parameter1, null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await GetNoParameters("GainMax");
                await GetNoParameters("GainMin");
                await GetNoParameters("Gains");
                await GetNoParameters("HasShutter");
                await GetNoParameters("HeatSinkTemperature");
                await GetNoParameters("ImageReady");
                await GetNoParameters("IsPulseGuiding");
                await GetNoParameters("MaxADU");
                await GetNoParameters("MaxBinX");
                await GetNoParameters("MaxBinY");
                await GetNoParameters("NumX");
                try { parameter1 = camera.NumX.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("NumX", "NumX", camera.NumX.ToString(), null);
                await GetNoParameters("NumY");
                try { parameter1 = camera.NumY.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("NumY", "NumY", camera.NumY.ToString(), null);
                await GetNoParameters("Offset");
                try { parameter1 = camera.Offset.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Offset", "Offset", parameter1, null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await GetNoParameters("OffsetMax");
                await GetNoParameters("OffsetMin");
                await GetNoParameters("Offsets");
                await GetNoParameters("PercentCompleted");
                await GetNoParameters("PixelSizeX");
                await GetNoParameters("PixelSizeY");
                await GetNoParameters("ReadoutMode");
                try { parameter1 = camera.ReadoutMode.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("ReadoutMode", "ReadoutMode", parameter1, null);
                await GetNoParameters("ReadoutModes");
                await GetNoParameters("SensorName");
                await GetNoParameters("SensorType");
                await GetNoParameters("SetCCDTemperature");
                try { parameter1 = camera.SetCCDTemperature.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("SetCCDTemperature", "SetCCDTemperature", parameter1, null);
                await GetNoParameters("StartX");
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                try { parameter1 = camera.StartX.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("StartX", "StartX", parameter1, null);
                await GetNoParameters("StartY");
                try { parameter1 = camera.StartY.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("StartY", "StartY", parameter1, null);
                await GetNoParameters("SubExposureDuration");
                try { parameter1 = camera.SubExposureDuration.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("SubExposureDuration", "SubExposureDuration", parameter1, null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                // Test methods
                await PutNoParameters("AbortExposure", null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await PutTwoParameters("PulseGuide", "Direction", ((int)GuideDirection.North).ToString(), "Duration", "1", null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await PutTwoParameters("StartExposure", "Duration", settings.CameraExposureDuration.ToString(), "Light", "False", () =>
                {
                    WaitWhile("StartExposure", () => { return camera.CameraState == CameraState.Exposing; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await PutNoParameters("StopExposure", null);
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                // Test properties that require an image to have been taken first

                // Add a base 64 hand-off header, if configured to do, so because this will speed up the JSON transfer by avoiding transmission of the entire image as JSON.
                if ((settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.Base64HandOff) | (settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.BestAvailable))
                {
                    httpClient.DefaultRequestHeaders.Add(AlpacaConstants.BASE64_HANDOFF_HEADER, new string[] { AlpacaConstants.BASE64_HANDOFF_SUPPORTED });
                }

                // Add an ImageBytes header, if configured to do, so because this will speed up the JSON transfer by avoiding transmission of the entire image as JSON.
                if ((settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.ImageBytes) | (settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.BestAvailable))
                {
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(AlpacaConstants.IMAGE_BYTES_MIME_TYPE));
                }

                SetStatus("Getting ImageArray...");
                await GetNoParameters("ImageArray");
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                SetStatus("Getting ImageArrayVariant...");
                await GetNoParameters("ImageArrayVariant");

                // Remove any added fast image download headers
                if ((settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.Base64HandOff) | (settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.BestAvailable))
                {
                    httpClient.DefaultRequestHeaders.Remove(AlpacaConstants.BASE64_HANDOFF_HEADER);
                }
                if ((settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.ImageBytes) | (settings.AlpacaConfiguration.ImageArrayTransferType == ImageArrayTransferType.BestAvailable))
                {
                    httpClient.DefaultRequestHeaders.Accept.Clear();
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(AlpacaConstants.APPLICATION_JSON_MIME_TYPE));
                }

                SetStatus("");
                if (applicationCancellationToken.IsCancellationRequested) goto CameraEnd; // Exit early if required

                await GetNoParameters("LastExposureDuration");
                await GetNoParameters("LastExposureStartTime");

            CameraEnd:
                try { camera.Connected = false; } catch { }

            }
        }

        private async Task TestCoverCalibrator()
        {
            string parameter1 = "";

            using (AlpacaCoverCalibrator coverCalibrator = AlpacaClient.GetDevice<AlpacaCoverCalibrator>(settings.AlpacaDevice))
            {
                try { coverCalibrator.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("Brightness");
                await GetNoParameters("CalibratorState");
                await GetNoParameters("CoverState");
                await GetNoParameters("MaxBrightness");
                if (applicationCancellationToken.IsCancellationRequested) goto CoverEnd; // Exit early if required

                // Test Methods
                await PutNoParameters("CalibratorOff", () =>
                {
                    WaitWhile("CalibratorOff", () => { return coverCalibrator.CalibratorState == CalibratorStatus.NotReady; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto CoverEnd; // Exit early if required

                try { parameter1 = (coverCalibrator.MaxBrightness / 2).ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("CalibratorOn", "Brightness", parameter1, () =>
                {
                    WaitWhile("CalibratorOn", () => { return coverCalibrator.CalibratorState == CalibratorStatus.NotReady; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto CoverEnd; // Exit early if required

                await PutNoParameters("OpenCover", () =>
                {
                    WaitWhile("OpenCover", () => { return coverCalibrator.CoverState == CoverStatus.Moving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto CoverEnd; // Exit early if required

                await PutNoParameters("HaltCover", () =>
                {
                    WaitWhile("HaltCover", () => { return coverCalibrator.CoverState == CoverStatus.Moving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto CoverEnd; // Exit early if required

                await PutNoParameters("CloseCover", () =>
                {
                    WaitWhile("CloseCover", () => { return coverCalibrator.CoverState == CoverStatus.Moving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });

            CoverEnd:
                try { coverCalibrator.Connected = false; } catch { }
            }
        }

        private async Task TestDome()
        {
            string parameter1 = "";

            using (AlpacaDome dome = AlpacaClient.GetDevice<AlpacaDome>(settings.AlpacaDevice))
            {
                try { dome.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("AtHome");
                await GetNoParameters("AtPark");
                await GetNoParameters("Azimuth");
                await GetNoParameters("CanFindHome");
                await GetNoParameters("CanPark");
                await GetNoParameters("CanSetAltitude");
                await GetNoParameters("CanSetAzimuth");
                await GetNoParameters("CanSetPark");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                await GetNoParameters("CanSetShutter");
                await GetNoParameters("CanSlave");
                await GetNoParameters("CanSyncAzimuth");
                await GetNoParameters("ShutterStatus");

                await GetNoParameters("Slaved");
                try { parameter1 = dome.Slaved.ToString(); } catch (Exception) { parameter1 = "false"; }
                await PutOneParameter("Slaved", "Slaved", parameter1, null);

                await GetNoParameters("Slewing");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                // Test Methods
                await PutNoParameters("AbortSlew", () =>
                {
                    WaitWhile("AbortSlew", () => { return dome.Slewing == true; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                await PutNoParameters("FindHome", () =>
                {
                    WaitWhile("FindHome", () => { return dome.Slewing == true; }, 500, settings.DomeAzimuthMovementTimeout, null);
                });
                WaitFor(settings.DomeStabilisationWaitTime * 1000, "dome azimuth movement delay");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                // Only open the shutter if configured to do so
                if (settings.DomeOpenShutter)
                {
                    await PutNoParameters("OpenShutter", () =>
                    {
                        WaitWhile("OpenShutter", () => { return dome.ShutterStatus == ShutterState.Opening; }, 500, settings.DomeShutterMovementTimeout, null);
                    });
                    WaitFor(settings.DomeStabilisationWaitTime * 1000, "open shutter delay");
                    if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required
                }
                else
                {
                    LogInformation($"PUT OpenShutter", "Test omitted due to Conform configuration setting", null);
                }

                if (settings.DomeOpenShutter)
                {
                    try { parameter1 = dome.Altitude.ToString(); } catch (Exception) { parameter1 = "45"; }
                    await PutOneParameter("SlewToAltitude", "Altitude", parameter1, () =>
                    {
                        WaitWhile("SlewToAltitude", () => { return dome.ShutterStatus == ShutterState.Opening; }, 500, settings.DomeAltitudeMovementTimeout, null);
                    });
                    WaitFor(settings.DomeStabilisationWaitTime * 1000, "dome altitude movement delay");
                    if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required
                }
                else
                {
                    LogInformation($"PUT SlewToAltitude", "Test omitted due to Conform configuration setting", null);
                }

                try { parameter1 = dome.Azimuth.ToString(); } catch (Exception) { parameter1 = "45"; }
                await PutOneParameter("SlewToAzimuth", "Azimuth", parameter1, () =>
                {
                    WaitWhile("SlewToAzimuth", () => { return dome.Slewing == true; }, 500, settings.DomeAzimuthMovementTimeout, null);
                });
                WaitFor(settings.DomeStabilisationWaitTime * 1000, "dome azimuth movement delay");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                try { parameter1 = dome.Azimuth.ToString(); } catch (Exception) { parameter1 = "45"; }
                await PutOneParameter("SyncToAzimuth", "Azimuth", parameter1, () =>
                {
                    WaitWhile("SyncToAzimuth", () => { return dome.Slewing == true; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });

                // Test property that can only be tested with the shutter open
                if (settings.DomeOpenShutter)
                {
                    await GetNoParameters("Altitude");
                }
                else
                {
                    LogInformation($"GET Altitude", "Test omitted due to Conform configuration setting", null);
                }

            DomeEnd:
                await PutNoParameters("CloseShutter", () =>
                {
                    WaitWhile("CloseShutter", () => { return dome.ShutterStatus == ShutterState.Closing; }, 500, settings.DomeShutterMovementTimeout, null);
                });
                WaitFor(settings.DomeStabilisationWaitTime * 1000, "close shutter delay");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                await PutNoParameters("Park", () =>
                {
                    WaitWhile("Park", () => { return dome.Slewing == true; }, 500, settings.DomeAzimuthMovementTimeout, null);
                });
                WaitFor(settings.DomeStabilisationWaitTime * 1000, "dome azimuth movement delay");
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                await PutNoParameters("SetPark", null);
                if (applicationCancellationToken.IsCancellationRequested) goto DomeEnd; // Exit early if required

                try { dome.Connected = false; } catch { }
            }
        }

        private async Task TestFilterWheel()
        {
            string parameter1 = "";

            using (AlpacaFilterWheel filterWheel = AlpacaClient.GetDevice<AlpacaFilterWheel>(settings.AlpacaDevice))
            {
                try { filterWheel.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("FocusOffsets");
                await GetNoParameters("Names");
                await GetNoParameters("Position");
                if (applicationCancellationToken.IsCancellationRequested) goto FilterWheelEnd; // Exit early if required

                try { parameter1 = filterWheel.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Position", "Position", parameter1, () =>
                {
                    WaitWhile("Position", () => { return filterWheel.Position == -1; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });

            FilterWheelEnd:
                try { filterWheel.Connected = false; } catch { }
            }
        }

        private async Task TestFocuser()
        {
            string parameter1 = "";

            using (AlpacaFocuser focuser = AlpacaClient.GetDevice<AlpacaFocuser>(settings.AlpacaDevice))
            {
                try { focuser.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("Absolute");
                await GetNoParameters("IsMoving");
                await GetNoParameters("MaxIncrement");
                await GetNoParameters("MaxStep");
                await GetNoParameters("Position");
                await GetNoParameters("StepSize");
                if (applicationCancellationToken.IsCancellationRequested) goto FocuserEnd; // Exit early if required

                await GetNoParameters("TempComp");
                try { parameter1 = focuser.TempComp.ToString(); } catch (Exception) { parameter1 = "false"; }
                await PutOneParameter("TempComp", "TempComp", parameter1, null);

                await GetNoParameters("TempCompAvailable");
                await GetNoParameters("Temperature");

                // Methods
                await PutNoParameters("Halt", () =>
                {
                    WaitWhile("Halt", () => { return focuser.IsMoving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto FocuserEnd; // Exit early if required

                try { parameter1 = focuser.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Move", "Position", parameter1, () =>
                {
                    WaitWhile("Move", () => { return focuser.IsMoving; }, 500, settings.FocuserTimeout, null);
                });

            FocuserEnd:
                try { focuser.Connected = false; } catch { }
            }
        }

        private async Task TestObservingConditions()
        {
            string parameter1 = "0.0";

            using (AlpacaObservingConditions observingConditions = AlpacaClient.GetDevice<AlpacaObservingConditions>(settings.AlpacaDevice))
            {
                try { observingConditions.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("AveragePeriod");
                try { parameter1 = observingConditions.AveragePeriod.ToString(); } catch { }
                await PutOneParameter("AveragePeriod", "AveragePeriod", parameter1, null);
                await GetNoParameters("CloudCover");
                await GetNoParameters("DewPoint");
                await GetNoParameters("Humidity");
                if (applicationCancellationToken.IsCancellationRequested) goto ObsConEnd; // Exit early if required

                await GetNoParameters("Pressure");
                await GetNoParameters("RainRate");
                await GetNoParameters("SkyBrightness");
                await GetNoParameters("SkyQuality");
                await GetNoParameters("SkyTemperature");
                if (applicationCancellationToken.IsCancellationRequested) goto ObsConEnd; // Exit early if required

                await GetNoParameters("StarFWHM");
                await GetNoParameters("Temperature");
                await GetNoParameters("WindDirection");
                await GetNoParameters("WindGust");
                await GetNoParameters("WindSpeed");
                if (applicationCancellationToken.IsCancellationRequested) goto ObsConEnd; // Exit early if required

                // Methods
                await PutNoParameters("Refresh", null);
                if (applicationCancellationToken.IsCancellationRequested) goto ObsConEnd; // Exit early if required

                await GetOneParameter("SensorDescription", "SensorName", "Pressure");
                await GetOneParameter("TimeSinceLastUpdate", "SensorName", "Pressure");

            ObsConEnd:
                try { observingConditions.Connected = false; } catch { }
            }
        }

        private async Task TestRotator()
        {
            string parameter1 = "";

            using (AlpacaRotator rotator = AlpacaClient.GetDevice<AlpacaRotator>(settings.AlpacaDevice))
            {
                try { rotator.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("CanReverse");
                await GetNoParameters("IsMoving");
                await GetNoParameters("MechanicalPosition");
                await GetNoParameters("Position");
                await GetNoParameters("Reverse");
                if (applicationCancellationToken.IsCancellationRequested) goto RotatorEnd; // Exit early if required

                try { parameter1 = rotator.Reverse.ToString(); } catch (Exception) { parameter1 = "false"; }
                await PutOneParameter("Reverse", "Reverse", parameter1, null);
                await GetNoParameters("StepSize");
                await GetNoParameters("TargetPosition");

                // Test methods
                await PutNoParameters("Halt", () =>
                {
                    WaitWhile("Halt", () => { return rotator.IsMoving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto RotatorEnd; // Exit early if required

                try { parameter1 = rotator.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Move", "Position", parameter1, () =>
                {
                    WaitWhile("Move", () => { return rotator.IsMoving; }, 500, settings.RotatorTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto RotatorEnd; // Exit early if required

                try { parameter1 = rotator.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("MoveAbsolute", "Position", parameter1, () =>
                {
                    WaitWhile("MoveAbsolute", () => { return rotator.IsMoving; }, 500, settings.RotatorTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto RotatorEnd; // Exit early if required

                try { parameter1 = rotator.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("MoveMechanical", "Position", parameter1, () =>
                {
                    WaitWhile("MoveMechanical", () => { return rotator.IsMoving; }, 500, settings.RotatorTimeout, null);
                });
                if (applicationCancellationToken.IsCancellationRequested) goto RotatorEnd; // Exit early if required

                try { parameter1 = rotator.Position.ToString(); } catch (Exception) { parameter1 = "1"; }
                await PutOneParameter("Sync", "Position", parameter1, () =>
                {
                    WaitWhile("Sync", () => { return rotator.IsMoving; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                });

            RotatorEnd:
                try { rotator.Connected = false; } catch { }
            }
        }

        private async Task TestSafetyMonitor()
        {
            using AlpacaSafetyMonitor safetyMonitor = AlpacaClient.GetDevice<AlpacaSafetyMonitor>(settings.AlpacaDevice);

            try { safetyMonitor.Connected = true; } catch { }

            // Test properties
            await GetNoParameters("IsSafe");

            // Disconnect
            try { safetyMonitor.Connected = false; } catch { }
        }

        private async Task TestSwitch()
        {
            string parameter1;

            using (AlpacaSwitch switchDevice = AlpacaClient.GetDevice<AlpacaSwitch>(settings.AlpacaDevice))
            {
                try { switchDevice.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("MaxSwitch");
                await GetOneParameter("CanWrite", "Id", "0");
                await GetOneParameter("GetSwitch", "Id", "0");
                WaitFor(settings.SwitchReadDelay, "switch read delay");

                await GetOneParameter("GetSwitchDescription", "Id", "0");
                if (applicationCancellationToken.IsCancellationRequested) goto SwitchEnd; // Exit early if required

                await GetOneParameter("GetSwitchName", "Id", "0");
                await GetOneParameter("GetSwitchValue", "Id", "0");
                WaitFor(settings.SwitchReadDelay, "switch read delay");

                await GetOneParameter("MinSwitchValue", "Id", "0");
                await GetOneParameter("MaxSwitchValue", "Id", "0");
                if (applicationCancellationToken.IsCancellationRequested) goto SwitchEnd; // Exit early if required

                // Test methods
                if (settings.SwitchEnableSet) // Test enabled
                {
                    try { parameter1 = switchDevice.GetSwitch(0).ToString(); } catch (Exception) { parameter1 = "false"; }
                    await PutTwoParameters("SetSwitch", "Id", "0", "State", parameter1, null);
                    WaitFor(settings.SwitchWriteDelay, "switch write delay");
                }
                else // Test omitted
                {
                    LogInformation($"PUT SetSwitch", "Test omitted due to Conform configuration setting", null);
                }
                if (applicationCancellationToken.IsCancellationRequested) goto SwitchEnd; // Exit early if required


                if (settings.SwitchEnableSet) // Test enabled
                {
                    try { parameter1 = switchDevice.GetSwitchName(0).ToString(); } catch (Exception) { parameter1 = "Unknown name"; }
                    await PutTwoParameters("SetSwitchName", "Id", "0", "Name", parameter1, null, testParameter2BadValue: false);
                }
                else // Test omitted
                {
                    LogInformation($"PUT SetSwitchName", "Test omitted due to Conform configuration setting", null);
                }
                if (applicationCancellationToken.IsCancellationRequested) goto SwitchEnd; // Exit early if required

                if (settings.SwitchEnableSet) // Test enabled
                {
                    try { parameter1 = switchDevice.GetSwitchValue(0).ToString(); } catch (Exception) { try { parameter1 = switchDevice.MinSwitchValue(0).ToString(); } catch { parameter1 = "0.0"; } }
                    await PutTwoParameters("SetSwitchValue", "Id", "0", "Value", parameter1, null);
                    WaitFor(settings.SwitchWriteDelay, "switch write delay");
                }
                else // Test omitted
                {
                    LogInformation($"PUT SetSwitchValue", "Test omitted due to Conform configuration setting", null);
                }

                await GetOneParameter("SwitchStep", "Id", "0");

            SwitchEnd:
                try { switchDevice.Connected = false; } catch { }
            }
        }

        private async Task TestTelescope()
        {
            string parameter1 = "";
            string parameter2 = "";

            using (AlpacaTelescope telescope = AlpacaClient.GetDevice<AlpacaTelescope>(settings.AlpacaDevice))
            {
                try { telescope.Connected = true; } catch { }

                // Test properties
                await GetNoParameters("AlignmentMode");
                await GetNoParameters("Altitude");
                await GetNoParameters("ApertureArea");
                await GetNoParameters("ApertureDiameter");
                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;

                await GetNoParameters("AtHome");
                await GetNoParameters("AtPark");
                await GetNoParameters("Azimuth");
                await GetNoParameters("CanPark");
                await GetNoParameters("CanPulseGuide");
                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;

                await GetNoParameters("CanSetDeclinationRate");
                await GetNoParameters("CanSetGuideRates");
                await GetNoParameters("CanSetPark");
                await GetNoParameters("CanSetPierSide");
                await GetNoParameters("CanSetTracking");
                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;

                await GetNoParameters("CanSlew");
                await GetNoParameters("CanSlewAltAz");
                await GetNoParameters("CanSlewAltAzAsync");
                await GetNoParameters("CanSync");
                await GetNoParameters("CanSyncAltAz");
                await GetNoParameters("CanUnpark");
                await GetNoParameters("Declination");
                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;

                await GetNoParameters("DeclinationRate");
                try { parameter1 = telescope.DeclinationRate.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("DeclinationRate", "DeclinationRate", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("DoesRefraction");
                try { parameter1 = telescope.DoesRefraction.ToString(); } catch (Exception) { parameter1 = "false"; }
                await PutOneParameter("DoesRefraction", "DoesRefraction", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("EquatorialSystem");
                await GetNoParameters("FocalLength");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("GuideRateDeclination");
                try { parameter1 = telescope.GuideRateDeclination.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("GuideRateDeclination", "GuideRateDeclination", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("GuideRateRightAscension");
                try { parameter1 = telescope.GuideRateRightAscension.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("GuideRateRightAscension", "GuideRateRightAscension", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("IsPulseGuiding");
                await GetNoParameters("RightAscension");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("RightAscensionRate");
                try { parameter1 = telescope.RightAscensionRate.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("RightAscensionRate", "RightAscensionRate", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("SideOfPier");
                try { parameter1 = ((int)telescope.SideOfPier).ToString(); } catch (Exception) { parameter1 = "0"; }
                await PutOneParameter("SideOfPier", "SideOfPier", parameter1, null);

                await GetNoParameters("SiderealTime");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("SiteElevation");
                try { parameter1 = telescope.SiteElevation.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("SiteElevation", "SiteElevation", parameter1, null);

                await GetNoParameters("SiteLatitude");
                try { parameter1 = telescope.SiteLatitude.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("SiteLatitude", "SiteLatitude", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("SiteLongitude");
                try { parameter1 = telescope.SiteLongitude.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("SiteLongitude", "SiteLongitude", parameter1, null);

                await GetNoParameters("Slewing");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("SlewSettleTime");
                try { parameter1 = telescope.SlewSettleTime.ToString(); } catch (Exception) { parameter1 = "0"; }
                await PutOneParameter("SlewSettleTime", "SlewSettleTime", parameter1, null);

                try { parameter1 = telescope.TargetDeclination.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("TargetDeclination", "TargetDeclination", parameter1, null);
                await GetNoParameters("TargetDeclination");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                try { parameter1 = telescope.TargetRightAscension.ToString(); } catch (Exception) { parameter1 = "0.0"; }
                await PutOneParameter("TargetRightAscension", "TargetRightAscension", parameter1, null);
                await GetNoParameters("TargetRightAscension");

                await GetNoParameters("Tracking");
                try { parameter1 = telescope.Tracking.ToString(); } catch (Exception) { parameter1 = "false"; }
                await PutOneParameter("Tracking", "Tracking", parameter1, null);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                await GetNoParameters("TrackingRate");
                try { parameter1 = ((int)telescope.TrackingRate).ToString(); } catch (Exception) { parameter1 = "0"; }
                await PutOneParameter("TrackingRate", "TrackingRate", parameter1, null);

                await GetNoParameters("UTCDate"); // Date format:                                                                   yyyy-MM-ddTHH:mm:ss.fffffffZ
                try { parameter1 = telescope.UTCDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"); } catch (Exception) { parameter1 = @"2022-12-04T17:45:31.1234567Z"; }
                await PutOneParameter("UTCDate", "UTCDate", parameter1, null);

                // Test Methods

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_PARK_UNPARK]) // Test enabled
                {
                    SetStatus("Parking scope...");
                    await PutNoParameters("Park", null);
                    WaitWhile("Park", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);

                    await PutNoParameters("SetPark", null);
                    await PutNoParameters("Unpark", null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_PARK_UNPARK}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_FIND_HOME]) // Test enabled
                {
                    SetStatus("Finding home...");
                    await PutNoParameters("FindHome", null);
                    WaitWhile("FindHome", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_FIND_HOME}", "Test omitted due to Conform configuration setting", null);
                }

                // Set tracking to TRUE for RA/Dec slews
                await CallApi("True", "Tracking", HttpMethod.Put, ParamTrackingTrue, HttpStatusCode200);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_ABORT_SLEW]) // Test enabled
                {
                    await PutNoParameters("AbortSlew", null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_ABORT_SLEW}", "Test omitted due to Conform configuration setting", null);
                }

                await GetOneParameter("AxisRates", "Axis", "0");
                await GetOneParameter("CanMoveAxis", "Axis", "0");

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                try { parameter1 = telescope.RightAscension.ToString(); } catch (Exception) { parameter1 = "21.0"; }
                try { parameter2 = telescope.Declination.ToString(); } catch (Exception) { parameter2 = "70"; }
                await GetTwoParameters("DestinationSideOfPier", "RightAscension", parameter1, "Declination", parameter2);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_MOVE_AXIS]) // Test enabled
                {
                    await PutTwoParameters("MoveAxis", "Axis", "0", "Rate", "0.0", null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_MOVE_AXIS}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_PULSE_GUIDE]) // Test enabled
                {
                    await PutTwoParameters("PulseGuide", "Direction", "0", "Duration", "0", () =>
                    {
                        WaitWhile("PulseGuide", () => { return telescope.IsPulseGuiding == true; }, 500, settings.AlpacaConfiguration.StandardResponseTimeout, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_PULSE_GUIDE}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_COORDINATES_ASYNC]) // Test enabled
                {
                    try { parameter1 = telescope.RightAscension.ToString(); } catch (Exception) { parameter1 = "21.0"; }
                    try { parameter2 = telescope.Declination.ToString(); } catch (Exception) { parameter2 = "70"; }
                    await PutTwoParameters("SlewToCoordinatesAsync", "RightAscension", parameter1, "Declination", parameter2, () =>
                    {
                        WaitWhile("SlewToCoordinatesAsync", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_COORDINATES_ASYNC}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_COORDINATES]) // Test enabled
                {
                    try { parameter1 = telescope.RightAscension.ToString(); } catch (Exception) { parameter1 = "12.0"; }
                    try { parameter2 = telescope.Declination.ToString(); } catch (Exception) { parameter2 = "80"; }
                    await PutTwoParameters("SlewToCoordinates", "RightAscension", parameter1, "Declination", parameter2, () =>
                    {
                        WaitWhile("SlewToCoordinates", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_COORDINATES}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_TARGET_ASYNC]) // Test enabled
                {
                    try { telescope.TargetRightAscension = telescope.RightAscension; } catch { }
                    try { telescope.TargetDeclination = telescope.Declination; } catch { }
                    await PutNoParameters("SlewToTargetAsync", () =>
                    {
                        WaitWhile("SlewToTargetAsync", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_TARGET_ASYNC}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_TARGET]) // Test enabled
                {
                    await PutNoParameters("SlewToTarget", () =>
                    {
                        WaitWhile("SlewToTarget", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_TARGET}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SYNC_TO_COORDINATES]) // Test enabled
                {
                    try { parameter1 = telescope.RightAscension.ToString(); } catch (Exception) { parameter1 = "45"; }
                    try { parameter2 = telescope.Declination.ToString(); } catch (Exception) { parameter2 = "45"; }
                    await PutTwoParameters("SyncToCoordinates", "RightAscension", parameter1, "Declination", parameter2, null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SYNC_TO_COORDINATES}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SYNC_TO_TARGET]) // Test enabled
                {
                    await PutNoParameters("SyncToTarget", null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SYNC_TO_TARGET}", "Test omitted due to Conform configuration setting", null);
                }

                // Set tracking to FALSE for Alt/Az slews
                await CallApi("False", "Tracking", HttpMethod.Put, ParamTrackingFalse, HttpStatusCode200);

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_ALTAZ_ASYNC]) // Test enabled
                {
                    try { parameter1 = telescope.Azimuth.ToString(); } catch (Exception) { parameter1 = "60"; }
                    try { parameter2 = telescope.Altitude.ToString(); } catch (Exception) { parameter2 = "60"; }
                    await PutTwoParameters("SlewToAltAzAsync", "Azimuth", parameter1, "Altitude", parameter2, () =>
                    {
                        WaitWhile("SlewToTargetAsync", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_ALTAZ_ASYNC}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SLEW_TO_ALTAZ]) // Test enabled
                {
                    try { parameter1 = telescope.Azimuth.ToString(); } catch (Exception) { parameter1 = "45"; }
                    try { parameter2 = telescope.Altitude.ToString(); } catch (Exception) { parameter2 = "45"; }
                    await PutTwoParameters("SlewToAltAz", "Azimuth", parameter1, "Altitude", parameter2, () =>
                    {
                        WaitWhile("SlewToTargetAsync", () => { return telescope.Slewing == true; }, 500, settings.TelescopeMaximumSlewTime, null);
                    });
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SLEW_TO_ALTAZ}", "Test omitted due to Conform configuration setting", null);
                }

                if (applicationCancellationToken.IsCancellationRequested) goto TelescopeEnd;
                if (settings.TelescopeTests[TelescopeTester.TELTEST_SYNC_TO_ALTAZ]) // Test enabled
                {
                    try { parameter1 = telescope.Azimuth.ToString(); } catch (Exception) { parameter1 = "45"; }
                    try { parameter2 = telescope.Altitude.ToString(); } catch (Exception) { parameter2 = "45"; }
                    await PutTwoParameters("SyncToAltAz", "Azimuth", parameter1, "Altitude", parameter2, null);
                }
                else // Test omitted
                {
                    LogInformation($"PUT {TelescopeTester.TELTEST_SYNC_TO_ALTAZ}", "Test omitted due to Conform configuration setting", null);
                }
                LogBlankLine();

            TelescopeEnd:
                // Set tracking to FALSE
                await CallApi("False", "Tracking", HttpMethod.Put, ParamTrackingFalse, HttpStatusCode200);

                try { telescope.Connected = false; } catch { }
            }
        }

        #endregion

        #region Test infrastructure

        private async Task GetNoParameters(string method)
        {
            // Test good ClientXXId name casing
            await CallApi("Good ClientID and ClientTransactionID casing", method, HttpMethod.Get, ParamsOk, HttpStatusCode200);

            // Test good ClientXXId name casing with extra parameter
            await CallApi("Good ClientID and ClientTransactionID casing with additional parameter", method, HttpMethod.Get, ParamsOkPlusExtraParameter, HttpStatusCode200);

            // Test incorrect ClientID casing
            await CallApi("Different ClientID casing", method, HttpMethod.Get, ParamClientIDLowerCase, HttpStatusCode200);

            // Test incorrect ClientTransactionID casing
            await CallApi("Different ClientTransactionID casing", method, HttpMethod.Get, ParamTransactionIdLowerCase, HttpStatusCode200);

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Get, null, null, null, null);
        }

        private async Task GetOneParameter(string method, string parameterName, string parameterValue, bool testParameterBadValue = true)
        {
            // Test good parameter name casing
            List<CheckProtocolParameter> goodParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Good casing)", method, HttpMethod.Get, goodParamCasing, HttpStatusCode200);

            // Test good parameter name casing with extra parameter
            List<CheckProtocolParameter> goodParamCasingPlusExtraParameter = new(ParamsOkPlusExtraParameter)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Good casing with extra parameter)", method, HttpMethod.Get, goodParamCasingPlusExtraParameter, HttpStatusCode200);

            // Test bad parameter value
            if (testParameterBadValue)
            {
                List<CheckProtocolParameter> badParamValue = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName, BAD_PARAMETER_VALUE)
                };
                await CallApi($"Parameter {parameterName} (Bad value)", method, HttpMethod.Get, badParamValue, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test incorrect parameter name casing
            List<CheckProtocolParameter> badParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(InvertCasing(parameterName), parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Inverted casing)", method, HttpMethod.Get, badParamCasing, HttpStatusCode200);

            // Test incorrect ClientID casing
            List<CheckProtocolParameter> differentClientIDCasing = new(ParamClientIDLowerCase)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi("Different ClientID casing", method, HttpMethod.Get, differentClientIDCasing, HttpStatusCode200);

            // Test incorrect ClientTransactionID casing
            List<CheckProtocolParameter> differentTransactionIdCasing = new(ParamTransactionIdLowerCase)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi("Different ClientTransactionID casing", method, HttpMethod.Get, differentTransactionIdCasing, HttpStatusCode200);

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Get, parameterName, parameterValue, null, null);
        }

        private async Task GetTwoParameters(string method, string parameterName1, string parameterValue1, string parameterName2, string parameterValue2, bool testParameter1BadValue = true, bool testParameter2BadValue = true)
        {
            // Test good parameter name casing
            List<CheckProtocolParameter> goodParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameter {parameterName1} and {parameterName2} (Good casing)", method, HttpMethod.Get, goodParamCasing, HttpStatusCode200);

            // Test good parameter name casing with extra parameter
            List<CheckProtocolParameter> goodParamCasingPlusExtraParameter = new(ParamsOkPlusExtraParameter)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameter {parameterName1} and {parameterName2} (Good casing with extra parameter)", method, HttpMethod.Get, goodParamCasingPlusExtraParameter, HttpStatusCode200);

            // Test bad parameter 1 value
            if (testParameter1BadValue)
            {
                List<CheckProtocolParameter> badParam1Value = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName1, BAD_PARAMETER_VALUE),
                    new CheckProtocolParameter(parameterName2, parameterValue2)
                };
                await CallApi($"Parameter {parameterName1} (Bad value)", method, HttpMethod.Get, badParam1Value, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test bad parameter 2 value
            if (testParameter2BadValue)
            {
                List<CheckProtocolParameter> badParam2Value = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName1, parameterValue1),
                    new CheckProtocolParameter(parameterName2, BAD_PARAMETER_VALUE)
                };
                await CallApi($"Parameter {parameterName2} (Bad value)", method, HttpMethod.Get, badParam2Value, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test bad parameter 1 name casing
            List<CheckProtocolParameter> badParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(InvertCasing(parameterName1), parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameter {parameterName1} (Inverted casing)", method, HttpMethod.Get, badParamCasing, HttpStatusCode200);

            // Test bad parameter 2 name casing
            badParamCasing = new List<CheckProtocolParameter>(ParamsOk)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(InvertCasing(parameterName2), parameterValue2)
            };
            await CallApi($"Parameter {parameterName2} (Inverted casing)", method, HttpMethod.Get, badParamCasing, HttpStatusCode200);

            // Test incorrect ClientID casing
            List<CheckProtocolParameter> differentClientIDCasing = new(ParamClientIDLowerCase)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi("Different ClientID casing", method, HttpMethod.Get, differentClientIDCasing, HttpStatusCode200);

            // Test incorrect ClientTransactionID casing
            List<CheckProtocolParameter> differentTransactionIdCasing = new(ParamTransactionIdLowerCase)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi("Different ClientTransactionID casing", method, HttpMethod.Get, differentTransactionIdCasing, HttpStatusCode200);

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Get, parameterName1, parameterValue1, parameterName2, parameterValue2);
        }

        private async Task PutNoParameters(string method, Action waitForCompletion)
        {
            // Test good ClientXXId name casing
            await CallApi("Good ID name casing", method, HttpMethod.Put, ParamsOk, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test good ClientXXId name casing with extra parameter
            await CallApi("Good ID casing + extra parameter", method, HttpMethod.Put, ParamsOkPlusExtraParameter, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test incorrect ClientID casing
            await CallApi("Bad ClientID casing", method, HttpMethod.Put, ParamClientIDLowerCase, HttpStatusCode200); // Must be 200 because the device should ignore the parameter if incorrectly cased
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test incorrect ClientTransactionID casing
            await CallApi("Bad ClientTransactionID casing", method, HttpMethod.Put, ParamTransactionIdLowerCase, HttpStatusCode200, badlyCasedTransactionIdName: true); // Must be 200 because the device should ignore the parameter if incorrectly cased
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Put, null, null, null, null);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required
        }

        private async Task PutOneParameter(string method, string parameterName, string parameterValue, Action waitForCompletion, bool testParameterBadValue = true)
        {
            // Test good parameter name casing
            List<CheckProtocolParameter> goodParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Good casing)", method, HttpMethod.Put, goodParamCasing, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test good parameter name casing with an extra parameter
            List<CheckProtocolParameter> goodParamCasingPlusExtraParameter = new(ParamsOkPlusExtraParameter)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Good casing + extra parameter)", method, HttpMethod.Put, goodParamCasingPlusExtraParameter, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion();  // Wait for completion if required

            // Test bad parameter value
            if (testParameterBadValue)
            {
                List<CheckProtocolParameter> badParamValue = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName, BAD_PARAMETER_VALUE)
                };
                await CallApi($"Parameter {parameterName} (Bad value)", method, HttpMethod.Put, badParamValue, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test bad parameter name casing
            List<CheckProtocolParameter> badParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(InvertCasing(parameterName), parameterValue)
            };
            await CallApi($"Parameter {parameterName} (Bad casing)", method, HttpMethod.Put, badParamCasing, HttpStatusCode400);
            if (waitForCompletion is not null) waitForCompletion();  // Wait for completion if required

            // Test bad ClientID casing
            List<CheckProtocolParameter> badClientIDCasing = new(ParamClientIDLowerCase)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi("Bad ClientID casing", method, HttpMethod.Put, badClientIDCasing, HttpStatusCode200); // Must be 200 because the device should ignore the parameter if incorrectly cased
            if (waitForCompletion is not null) waitForCompletion();  // Wait for completion if required

            // Test bad ClientTransactionID casing
            List<CheckProtocolParameter> badTransactionIdCasing = new(ParamTransactionIdLowerCase)
            {
                new CheckProtocolParameter(parameterName, parameterValue)
            };
            await CallApi("Bad ClientTransactionID casing", method, HttpMethod.Put, badTransactionIdCasing, HttpStatusCode200, badlyCasedTransactionIdName: true); // Must be 200 because the device should ignore the parameter if incorrectly cased
            if (waitForCompletion is not null) waitForCompletion();  // Wait for completion if required

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Put, parameterName, parameterValue, null, null);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required
        }

        private async Task PutTwoParameters(string method, string parameterName1, string parameterValue1, string parameterName2, string parameterValue2, Action waitForCompletion, bool testParameter1BadValue = true, bool testParameter2BadValue = true)
        {
            // Test good parameter name casing
            List<CheckProtocolParameter> goodParamCasing = new(ParamsOk)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameters {parameterName1} and {parameterName2} (Good casing)", method, HttpMethod.Put, goodParamCasing, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test good parameter name casing with an additional parameter
            List<CheckProtocolParameter> goodParamCasingPlusExtraParameter = new(ParamsOkPlusExtraParameter)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameters {parameterName1} and {parameterName2} (Good casing with extra parameter)", method, HttpMethod.Put, goodParamCasingPlusExtraParameter, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad parameter 1 value
            if (testParameter1BadValue)
            {
                List<CheckProtocolParameter> badParam1Value = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName1, BAD_PARAMETER_VALUE),
                    new CheckProtocolParameter(parameterName2, parameterValue2)
                };
                await CallApi($"Parameter {parameterName1} (Bad value)", method, HttpMethod.Put, badParam1Value, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test bad parameter 2 value
            if (testParameter2BadValue)
            {
                List<CheckProtocolParameter> badParam2Value = new(ParamsOk)
                {
                    new CheckProtocolParameter(parameterName1, parameterValue1),
                    new CheckProtocolParameter(parameterName2, BAD_PARAMETER_VALUE)
                };
                await CallApi($"Parameter {parameterName2} (Bad value)", method, HttpMethod.Put, badParam2Value, HttpStatusCode400, acceptInvalidValueError: true);
            }

            // Test bad parameter 1 name casing
            List<CheckProtocolParameter> badParam1Casing = new(ParamsOk)
            {
                new CheckProtocolParameter(InvertCasing(parameterName1), parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi($"Parameter {parameterName1} (Bad casing)", method, HttpMethod.Put, badParam1Casing, HttpStatusCode400);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad parameter 2 name casing
            List<CheckProtocolParameter> badParam2Casing = new(ParamsOk)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(InvertCasing(parameterName2), parameterValue2)
            };
            await CallApi($"Parameter {parameterName2} (Bad casing)", method, HttpMethod.Put, badParam2Casing, HttpStatusCode400);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad ClientID casing
            List<CheckProtocolParameter> badClientIDCasing = new(ParamClientIDLowerCase)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi("Bad ClientID casing", method, HttpMethod.Put, badClientIDCasing, HttpStatusCode200);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad ClientTransactionID casing
            List<CheckProtocolParameter> badTransactionIdCasing = new(ParamTransactionIdLowerCase)
            {
                new CheckProtocolParameter(parameterName1, parameterValue1),
                new CheckProtocolParameter(parameterName2, parameterValue2)
            };
            await CallApi("Bad ClientTransactionID casing", method, HttpMethod.Put, badTransactionIdCasing, HttpStatusCode200, badlyCasedTransactionIdName: true);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required

            // Test bad client and transaction Id values
            await TestBadIdValues(method, HttpMethod.Put, parameterName1, parameterValue1, parameterName2, parameterValue2);
            if (waitForCompletion is not null) waitForCompletion(); // Wait for completion if required
        }

        /// <summary>
        /// Test correctly cased ClientID and ClientTransactionID parameters that have varying types of bad values
        /// </summary>
        /// <param name="method"></param>
        /// <param name="httpMethod"></param>
        /// <param name="parameterName1"></param>
        /// <param name="parameterValue1"></param>
        /// <param name="parameterName2"></param>
        /// <param name="parameterValue2"></param>
        /// <returns></returns>
        /// <remarks>These shoul all fail.</remarks>
        private async Task TestBadIdValues(string method, HttpMethod httpMethod, string parameterName1, string parameterValue1, string parameterName2, string parameterValue2)
        {
            List<CheckProtocolParameter> parameters;

            // Test empty ClientID value
            parameters = new List<CheckProtocolParameter>(ParamClientIDEmpty);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientID is empty", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test white space ClientID value
            parameters = new List<CheckProtocolParameter>(ParamClientIDWhiteSpace);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientID is white space", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test negative ClientID value
            parameters = new List<CheckProtocolParameter>(ParamClientIDNegative);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientID is negative", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test text ClientID value
            parameters = new List<CheckProtocolParameter>(ParamClientIDString);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientID is a string", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test empty ClientTransactionID value
            parameters = new List<CheckProtocolParameter>(ParamTransactionIdEmpty);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientTransactionID is empty", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test white space ClientTransactionID value
            parameters = new List<CheckProtocolParameter>(ParamTransactionIdWhiteSpace);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientTransactionID is white space", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test negative ClientTransactionID value
            parameters = new List<CheckProtocolParameter>(ParamTransactionIdNegative);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientTransactionID is negative", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);

            // Test text ClienClientTransactionIDtId value
            parameters = new List<CheckProtocolParameter>(ParamTransactionIdString);
            if (parameterName1 is not null) parameters.Add(new CheckProtocolParameter(parameterName1, parameterValue1));
            if (parameterName2 is not null) parameters.Add(new CheckProtocolParameter(parameterName2, parameterValue2));
            await CallApi("ClientTransactionID is a string", method, httpMethod, parameters, HttpStatusCode400, acceptInvalidValueError: true);
        }

        private async Task CallApi(string messagePrefix,
                                   string method,
                                   HttpMethod httpMethod,
                                   List<CheckProtocolParameter> parameters,
                                   List<HttpStatusCode> expectedCodes,
                                   bool ignoreApplicationCancellation = false,
                                   bool badlyCasedTransactionIdName = false,
                                   bool acceptInvalidValueError = false)
        {
            string methodLowerCase = method.ToLowerInvariant();
            string httpMethodUpperCase = httpMethod.ToString().ToUpperInvariant();

            string url = $"/api/v1/{settings.DeviceType.ToString().ToLowerInvariant()}/{settings.AlpacaDevice.AlpacaDeviceNumber}/{methodLowerCase}";
            await SendToDevice($"{httpMethodUpperCase} {method}", messagePrefix, url, httpMethod, parameters, expectedCodes, ignoreApplicationCancellation, badlyCasedTransactionIdName, acceptInvalidValueError);
        }

        private async Task SendToDevice(string testName,
                                        string messagePrefix,
                                        string url,
                                        HttpMethod httpMethod,
                                        List<CheckProtocolParameter> parameters,
                                        List<HttpStatusCode> expectedCodes,
                                        bool ignoreApplicationCancellation = false,
                                        bool badlyCasedTransactionIdName = false,
                                        bool acceptInvalidValueError = false)
        {
            string ascomOutcome = null;
            CancellationTokenSource requestCancellationTokenSource;
            Response deviceResponse = new(); // Parsed JSON response

            bool hasClientTransactionID = false;
            uint returnedClientTransactionID = 0;
            uint expectedClientTransactionID = 0; // The expected ClientTransactionID round trip value
            uint returnedServerTransactionID = 0;
            AlpacaErrors returnedErrorNumber = AlpacaErrors.AlpacaNoError;
            string returnedErrorMessage = "";

            if (expectedCodes is null)
            {
                throw new ArgumentNullException(nameof(expectedCodes));
            }

            try
            {
                string clientHostAddress = $"{settings.AlpacaDevice.ServiceType.ToString().ToLowerInvariant()}://{settings.AlpacaDevice.IpAddress}:{settings.AlpacaDevice.IpPort}";

                // Create the URI for this transaction and apply it to the request, adding "client id" and "transaction number" query parameters
                UriBuilder transactionUri = new($"{clientHostAddress}{url}");

                HttpRequestMessage request;

                #region Prepare and send request

                // Prepare HTTP GET and PUT requests
                if (httpMethod == HttpMethod.Get) // HTTP GET methods
                {
                    // Add to the query string any further required parameters for HTTP GET methods
                    if (parameters.Count > 0)
                    {
                        foreach (CheckProtocolParameter parameter in parameters)
                        {
                            transactionUri.Query = $"{transactionUri.Query}&{parameter.ParameterName}={parameter.ParameterValue}".TrimStart('&');

                            // Test whether we have a correctly cased ClientTransactionID parameter and if so flag this because the value should round-trip OK
                            if (parameter.ParameterName.ToUpperInvariant() == "ClientTransactionID".ToUpperInvariant())
                            {
                                // Record that we have a valid ClientTransactionID parameter name
                                hasClientTransactionID = true;

                                // Extract the expected value if possible. 0 indicates no value or an invalid value which is not expected to round trip
                                _ = UInt32.TryParse(parameter.ParameterValue, out expectedClientTransactionID);
                                TL.LogMessage(testName, MessageLevel.Debug, $"{messagePrefix} - Input ClientTransactionID value: {parameter.ParameterValue}, Parsed value: {expectedClientTransactionID}");
                            }
                        }
                    }

                    // Create a new request based on the transaction Uri
                    request = new HttpRequestMessage(httpMethod, transactionUri.Uri);

                } // Prepare GET requests
                else // Prepare PUT and all other HTTP method requests
                {
                    // Create a new request based on the transaction Uri
                    request = new HttpRequestMessage(httpMethod, transactionUri.Uri);

                    // Add all parameters to the request body as form URL encoded content
                    if (parameters.Count > 0)
                    {
                        Dictionary<string, string> formParameters = new();
                        foreach (CheckProtocolParameter parameter in parameters)
                        {
                            formParameters.Add(parameter.ParameterName, parameter.ParameterValue);

                            // Test whether we have a correctly cased ClientTransactionID parameter and if so flag this because the value should round-trip OK
                            if (parameter.ParameterName.ToUpperInvariant() == "ClientTransactionID".ToUpperInvariant())
                            {
                                // Record that we have a ClientTransactionID parameter name, which may or may not be correctly cased
                                hasClientTransactionID = true;

                                // Test whether the parameter name is correctly cased
                                if (parameter.ParameterName == "ClientTransactionID") // It is correctly cased
                                {
                                    // Extract the expected value if possible. 0 indicates no value or an invalid value which is not expected to round trip
                                    _ = UInt32.TryParse(parameter.ParameterValue, out expectedClientTransactionID);
                                    TL.LogMessage(testName, MessageLevel.Debug, $"{messagePrefix} - Input ClientTransactionID name is correctly cased: {parameter.ParameterValue}, Parsed value: {expectedClientTransactionID}");

                                }
                                else // It is not correctly cased.
                                {
                                    expectedClientTransactionID = 0;
                                    TL.LogMessage(testName, MessageLevel.Debug, $"{messagePrefix} - Input ClientTransactionID name is NOT correctly cased: {parameter.ParameterValue}, Expected value: {expectedClientTransactionID}");
                                }
                            }
                        }

                        FormUrlEncodedContent formUrlParameters = new(formParameters);
                        request.Content = formUrlParameters;
                    }
                } // Prepare PUT requests

                // Create a cancellation token source that either respects or ignores the application cancellation state
                if (ignoreApplicationCancellation) // Ignore application cancellation state (used for commands that must be sent under all circumstances e.g. setting Connected to False)
                {
                    requestCancellationTokenSource = new CancellationTokenSource();
                }
                else // Respect the application cancellation state
                {
                    // Ignore this request if the application is already cancelled
                    if (applicationCancellationToken.IsCancellationRequested) return;

                    // Create a combined cancellation taken that will trigger when either the application is cancelled or the request times out.
                    requestCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(applicationCancellationTokenSource.Token);
                }

                // Set the token source to time out after the specified interval
                requestCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(settings.AlpacaConfiguration.LongResponseTimeout));

                // Send the request to the remote device and wait for the response
                HttpResponseMessage httpResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, requestCancellationTokenSource.Token);

                #endregion

                // Get the device response
                string responseString = await httpResponse.Content.ReadAsStringAsync();

                #region Process successful HTTP status = 200 responses

                // If the call was successful at an HTTP level (Status = 200) check whether there was an ASCOM error reported in the returned JSON
                if ((httpResponse.StatusCode == HttpStatusCode.OK) & (!responseString.Contains("<!DOCTYPE")))
                {
                    // Response should be either a JSON or an ImageBytes response
                    try
                    {
                        // Handle the response, which will be either JSON or ImageBytes
                        if (!httpResponse.Content.Headers.ContentType.MediaType.Contains(AlpacaConstants.IMAGE_BYTES_MIME_TYPE, StringComparison.InvariantCultureIgnoreCase)) // Should be a JSON response
                        {

                            // Parse the common device values from the JSON response into a Response class
                            deviceResponse = JsonSerializer.Deserialize<Response>(responseString);

                            // Save the parsed response values
                            returnedClientTransactionID = deviceResponse.ClientTransactionID;
                            returnedServerTransactionID = deviceResponse.ServerTransactionID;
                            returnedErrorNumber = deviceResponse.ErrorNumber;
                            returnedErrorMessage = deviceResponse.ErrorMessage;

                        } // Handle a JSON response
                        else // Should be an ImageBytes binary response
                        {
                            // Convert the string response to a byte array
                            byte[] bytes = Encoding.ASCII.GetBytes(responseString);

                            // Get the metadata version
                            int metadataVersion = bytes.GetMetadataVersion();

                            // Handle version number possibilities
                            switch (metadataVersion)
                            {
                                case 1: // The only valid version at present
                                    LogOk(testName, $"{messagePrefix} - The expected ImageBytes metadata version was returned: {metadataVersion}", null);
                                    break;

                                default: // All other values
                                    LogIssue(testName, $"{messagePrefix} - An unexpected ImageBytes metadata version was returned: {metadataVersion}, Expected: 1", null);
                                    break;
                            }

                            // Get the metadata from the response bytes
                            ArrayMetadataV1 metadata = bytes.GetMetadataV1();

                            // Save the response values in the returned metadata
                            returnedClientTransactionID = metadata.ClientTransactionID;
                            returnedServerTransactionID = metadata.ServerTransactionID;
                            returnedErrorNumber = metadata.ErrorNumber;

                            // Get the error message if required
                            if (returnedErrorNumber != AlpacaErrors.AlpacaNoError)
                            {
                                returnedErrorMessage = bytes.GetErrrorMessage();
                            }

                            // Set a useful response string because otherwise we will get the enormous number of AlpacaBytes binary values encoded as a string
                            responseString = $"ClientTransactionID: {returnedClientTransactionID}, ServerTransactionID: {returnedServerTransactionID}, ErrorNumber: {returnedErrorNumber}, ErrorMessage: '{returnedErrorMessage}'";

                        } // Handle an ImageBytes response
                    }
                    catch (Exception ex)
                    {
                        LogIssue(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}) but could not de-serialise the returned JSON string. Exception message: {ex.Message}", responseString);
                        LogBlankLine();
                        return;
                    }
                }

                #endregion

                #region Determine success or failure of the test

                // Test whether the ClientTransactionID round tripped OK
                if (hasClientTransactionID & (httpResponse.StatusCode == HttpStatusCode.OK)) // A client transaction ID parameter was sent and the transaction was processed OK
                {
                    // Test whether the expected value was returned
                    if (returnedClientTransactionID == expectedClientTransactionID) // Round tripped OK
                    {
                        LogOk(testName, $"{messagePrefix} - The expected ClientTransactionID was returned: {returnedClientTransactionID}", null);
                    }
                    else // Did not round trip OK
                    {
                        // Handle responses to a badly cased ClientTransactionID FORM parameter
                        if (badlyCasedTransactionIdName) // This transaction does contain a badly cased ClientTransactionID FORM parameter
                        {
                            // Check whether the expected value of 0 was returned. 
                            if (returnedClientTransactionID == 0) // Got the expected value of 0
                            {
                                LogOk(testName, $"{messagePrefix} - The ClientTransactionID was round-tripped as expected. Sent value: {expectedClientTransactionID}, Returned value: {returnedClientTransactionID}", null);
                            }
                            else // Got some value other than expected value of 0
                            {
                                LogIssue(testName, $"{messagePrefix} - An unexpected ClientTransactionID was returned: {returnedClientTransactionID}, Expected: {expectedClientTransactionID}", null);
                            }
                        }
                        else // This transaction does not contain a badly cased ClientTransactionID FORM parameter so report an issue
                        {
                            LogIssue(testName, $"{messagePrefix} - An unexpected ClientTransactionID was returned: {returnedClientTransactionID}, Expected: {expectedClientTransactionID}", null);
                        }
                    }
                }

                // Test whether a valid ServerTransactionID value was returned
                if (httpResponse.StatusCode == HttpStatusCode.OK) // We got an HTTP 200 OK status
                {
                    if (returnedServerTransactionID >= 1)  // Valid ServerTransactionID
                    {
                        LogOk(testName, $"{messagePrefix} - The ServerTransactionID was 1 or greater: {returnedServerTransactionID}", null);
                    }
                    else // Invalid ServerTransactionID
                    {
                        LogIssue(testName, $"{messagePrefix} - An unexpected ServerTransactionID was returned: {returnedServerTransactionID}, Expected: 1 or greater", null);
                    }
                }

                // Test whether the device reported an error
                if ((returnedErrorNumber != 0) | (returnedErrorMessage != "")) // An error was returned
                {
                    // Create a message indicating what went wrong.
                    try
                    {
                        // Only report not implemented errors if configured to do so
                        if ((returnedErrorNumber == AlpacaErrors.NotImplemented) & !settings.AlpacaConfiguration.ProtocolReportNotImplementedErrors)
                        {
                            // Do nothing because we are not reporting not implemented errors
                        }
                        else
                        {
                            ascomOutcome = $"Device returned a {returnedErrorNumber} error (0x{returnedErrorNumber:X}) for client transaction: {returnedClientTransactionID}, " +
                                $"server transaction: {returnedServerTransactionID}. " +
                                $"Error message: {returnedErrorMessage}";
                        }
                    }
                    catch (Exception) // Handle possibility of a non-ASCOM error number
                    {
                        ascomOutcome = $"Device returned error number 0x{returnedErrorNumber:X} for client transaction: {returnedClientTransactionID}, server transaction: {returnedServerTransactionID}. " +
                            $"Error message: {returnedErrorMessage}";
                    }
                }

                // Check whether any specific HTTP status codes are expected
                if (expectedCodes.Count > 0) // One or more codes that indicate a successful test are expected
                {
                    // Check whether we got an expected or unexpected status code
                    if (expectedCodes.Contains(httpResponse.StatusCode)) // We got one of the expected outcomes
                    {
                        // Check whether we got a 200 OK status or something else
                        if (httpResponse.StatusCode == HttpStatusCode.OK) // Received a 200 OK status
                        {
                            // Log an OK outcome if there was no contextual message or an Information if there was
                            if (string.IsNullOrEmpty(ascomOutcome)) // No contextual message
                            {
                                LogOk(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}) as expected.", responseString);
                            }
                            else // Does have a contextual message
                            {
                                LogInformation(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}) as expected but the device reported an ASCOM error:", ascomOutcome);
                            }
                        }
                        else // Received a status code other than 200 OK
                        {
                            LogOk(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}) as expected.", responseString);
                        }
                    }
                    else // Unexpected outcome
                    {
                        // Test whether we got an InvalidValue error and are going to accept that
                        if ((httpResponse.StatusCode == HttpStatusCode.OK) & acceptInvalidValueError & (deviceResponse.ErrorNumber == AlpacaErrors.InvalidValue)) // We are going to accept an InvalidValue error
                        {
                            LogOk(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}) and an invalid value error: {deviceResponse.ErrorMessage}", responseString);
                        }
                        else // Did not get the expected response so log this as an issue
                        {
                            string expectedCodeList = "";
                            foreach (HttpStatusCode statusCode in expectedCodes)
                            {
                                expectedCodeList += $"{(int)statusCode} ({statusCode}), ";
                            }
                            expectedCodeList = expectedCodeList.TrimEnd(' ', ',');

                            LogIssue(testName, $"{messagePrefix} - Expected HTTP status{(expectedCodeList.Contains(',') ? "es" : "")}: {expectedCodeList.Trim()} but received status: " +
                                $"{(int)httpResponse.StatusCode} ({httpResponse.StatusCode}).", responseString);

                            LogBlankLine();
                        }
                    }
                }
                else // There are no expected codes, which indicates that any code is acceptable
                {
                    LogInformation(testName, $"{messagePrefix} - Received HTTP status {(int)httpResponse.StatusCode} ({httpResponse.StatusCode})", responseString);
                }

                #endregion

            }
            catch (TaskCanceledException)
            {
                LogError(testName, $"{messagePrefix} - The HTTP request was cancelled", null);
            }
            catch (HttpRequestException ex)
            {
                LogError(testName, $"{messagePrefix} - {ex.Message}", null);
            }
            catch (Exception ex)
            {
                LogError(testName, $"{messagePrefix} - {ex}", null);
            }
        }

        #endregion

        #region Support code

        static string InvertCasing(string s)
        {
            char[] c = s.ToCharArray();
            char[] cUpper = s.ToUpper().ToCharArray();
            char[] cLower = s.ToLower().ToCharArray();

            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] == cUpper[i])
                {
                    c[i] = cLower[i];
                }
                else
                {
                    c[i] = cUpper[i];
                }
            }

            return new string(c);
        }

        /// <summary>
        /// Call the wait function every poll interval milliseconds and delay until the wait function becomes false
        /// </summary>
        /// <param name="actionName">Text to set in the status Action field</param>
        /// <param name="waitFunction">Completion Func that returns false when the process is complete</param>
        /// <param name="pollInterval">Interval between calls of the completion function in milliseconds</param>
        /// <param name="timeoutSeconds">Number of seconds before the operation times out</param>
        /// <exception cref="InvalidValueException"></exception>
        /// <exception cref="TimeoutException">If the operation takes longer than the timeout value</exception>
        internal void WaitWhile(string actionName, Func<bool> waitFunction, int pollInterval, int timeoutSeconds, Func<string> statusString)
        {
            // Validate the supplied poll interval
            if (pollInterval < 100) throw new ASCOM.InvalidValueException($"The poll interval must be >=100ms: {pollInterval}");

            // Initialise the status message
            if (statusString is null)
                SetStatus($"Waiting for the {actionName} operation to complete: 0.0 / {timeoutSeconds:0.0} seconds");
            else
                SetStatus($"Waiting for the {actionName} operation to complete: {statusString()}");

            // Create a timeout cancellation token source that times out after the required timeout period
            CancellationTokenSource timeoutCts = new();
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Convert.ToDouble(timeoutSeconds) + 2.0 * (Convert.ToDouble(pollInterval) / 1000.0))); // Allow two poll intervals beyond the timeout time to prevent early termination

            // Combine the provided cancellation token parameter with the new timeout cancellation token
            CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, applicationCancellationToken);

            // Wait for the completion function to return false
            Stopwatch sw = Stopwatch.StartNew(); // Start the loop timing stopwatch
            while (waitFunction() & !combinedCts.Token.IsCancellationRequested)
            {
                // Calculate the current loop number (starts at 0 given that the timer's elapsed time will be zero or very low on the first loop)
                int currentLoopNumber = ((int)(sw.ElapsedMilliseconds) + 50) / pollInterval; // Add a small positive offset (50) because integer division always rounds down

                // Calculate the sleep time required to start the next loop at a multiple of the poll interval
                int sleepTime = pollInterval * (currentLoopNumber + 1) - (int)sw.ElapsedMilliseconds;

                // Sleep until it is time for the next completion function poll
                Thread.Sleep(sleepTime);

                // Set the status message status field
                if (statusString is null) // No status string function was provided so display an elapsed time message
                    SetStatus($"Waiting for the {actionName} operation to complete: {Math.Round(Convert.ToDouble(currentLoopNumber + 1) * pollInterval / 1000.0, 1):0.0} / {timeoutSeconds:0.0} seconds");
                else // Display the supplied message instead of the elapsed time message
                    SetStatus($"Waiting for the {actionName} operation to complete: {statusString()}");
            }

            // Test whether the operation timed out
            if (timeoutCts.IsCancellationRequested) // The operation did time out
            {
                //  Log the timeout and throw an exception to cancel the operation
                LogIssue("WaitUntil", $"The {actionName} operation timed out after {timeoutSeconds} seconds.", null);
                throw new TimeoutException($"The \"{actionName}\" operation exceeded the timeout of {timeoutSeconds} seconds specified for this operation.");
            }

            SetStatus("");
        }

        /// <summary>
        /// Delay execution for the given time period in milliseconds
        /// </summary>
        /// <param name="waitDuration">Period to wait in milliseconds</param>
        /// <param name="updateInterval">Optional interval between status updates(Default 500ms)</param>
        /// <remarks></remarks>
        internal void WaitFor(int waitDuration, string purpose, int updateInterval = 500)
        {
            if (waitDuration > 0)
            {
                // Ensure that we don't wait more than the expected duration
                if (updateInterval > waitDuration) updateInterval = waitDuration;

                // Initialise the status message status field
                SetStatus($"Waiting for {purpose} - 0.0 / {Convert.ToDouble(waitDuration) / 1000.0:0.0} seconds");

                // Start the loop timing stopwatch
                Stopwatch sw = Stopwatch.StartNew();

                // Wait for p_Duration milliseconds
                do
                {
                    // Calculate the current loop number (starts at 1 given that the timer's elapsed time will be zero or very low on the first loop)
                    int currentLoopNumber = ((int)sw.ElapsedMilliseconds + 50) / updateInterval;

                    // Calculate the sleep time required to start the next loop at a multiple of the poll interval
                    int sleepTime = updateInterval * (currentLoopNumber + 1) - (int)sw.ElapsedMilliseconds;

                    // Ensure that we don't over-wait on the last cycle
                    int remainingWaitTime = waitDuration - (int)sw.ElapsedMilliseconds;
                    if (remainingWaitTime < 0) remainingWaitTime = 0;
                    if (remainingWaitTime < updateInterval) sleepTime = remainingWaitTime;

                    // Sleep until it is time for the next completion function poll
                    Thread.Sleep(sleepTime);

                    // Set the status message status field to the elapsed time
                    SetStatus($"Waiting for {purpose} - {Math.Round(Convert.ToDouble(currentLoopNumber + 1) * updateInterval / 1000.0, 1):0.0} / {Convert.ToDouble(waitDuration) / 1000.0:0.0} seconds");
                }
                while ((sw.ElapsedMilliseconds <= waitDuration) & !applicationCancellationToken.IsCancellationRequested);
            }
        }

        /// <summary>
        /// Set the isOmniSim flag if this device is the Alpaca Omni-simulator.
        /// </summary>
        //internal void TestForOmnisim(IAscomDevice device)
        //{
        //    string prefix = "";
        //    string deviceType = "";
        //    string postFix = "";
        //    bool hasRequiredParts = false;

        //    // Get the device name
        //    string deviceName = device.Name;

        //    // Default to false
        //    isOmniSim = false;

        //    // Split the returned name into its space separated parts
        //    string[] nameParts = deviceName.Split(' ');

        //    // The Omni-simulator has a 3 part name: "Alpaca DeviceType Simulator" or a 4 part name: "Alpaca Device Type Simulator" so test for these
        //    //                                        111111 2222222222 333333333                     111111 222222 3333 444444444

        //    // Parse out the prefix, device type and postfix from 3 and 4 part device names
        //    // Device type names that are supplied in 2 parts, such as "Filter Wheel", are concatenated to a deviceType single value e.g. "Filter Wheel" becomes "FilterWheel"
        //    switch (nameParts.Length)
        //    {
        //        case 3:
        //            prefix = nameParts[0];
        //            deviceType = nameParts[1];
        //            postFix = nameParts[2];
        //            hasRequiredParts = true;
        //            break;

        //        case 4:
        //            prefix = nameParts[0];
        //            deviceType = nameParts[1] + nameParts[2];
        //            postFix = nameParts[3];
        //            hasRequiredParts = true;
        //            break;

        //        default:
        //            break;
        //    }

        //    // Test whether the name has number of parts
        //    if (hasRequiredParts) // Has the required parts to undertake the comparison
        //    {
        //        // Test whether the prefix is "Alpaca", the device type is a valid device type and the postfix 3 is "Simulator".
        //        if ((prefix == "Alpaca") & (Devices.IsValidDeviceTypeName(deviceType)) & (postFix == "Simulator")) // The value returned by the device matches the value expected from the OmniSim
        //        {
        //            // Set the OmniSim flag because we are testing an OmniSim device.
        //            isOmniSim = true;
        //        }
        //        else // The value returned by the device does not match the value expected from the OmniSim
        //        {
        //            // No action required because the default state is false
        //        }
        //    }
        //}

        #endregion

        #region Logging and window resize

        /// <summary>
        /// Log a formatted message to the screen and log file
        ///</summary>
        ///<param name="method">Calling method name</param>
        ///    <param name = "message" > Message to log</param>
        ///    <param name = "padding" > Number of characters to which the message should be right padded</param>
        private void LogMessage(string method, TestOutcome outcome, string message, string contextMessage = null)
        {
            string methodString, outcomeString;

            switch (outcome)
            {
                case TestOutcome.OK:
                    outcomeString = "OK";
                    break;

                case TestOutcome.Info:
                    outcomeString = "INFO";
                    break;

                case TestOutcome.Issue:
                    outcomeString = "ISSUE";
                    break;
                case TestOutcome.Error:
                    outcomeString = "ERROR";
                    break;

                default:
                    throw new Exception($"Unknown test outcome type: {outcome}");
            }
            methodString = $"{method,-ExtensionMethods.COLUMN_WIDTH}{outcomeString,-ExtensionMethods.OUTCOME_WIDTH}";

            // Write to the logger
            TL?.LogMessage(methodString, message);

            // Add the context message if present
            if (contextMessage is not null)
            {
                TL?.LogMessage(methodString, $"  Response: {contextMessage}");
            }
        }

        /// <summary>
        /// Log an unformatted message to the screen and log file
        /// </summary>
        /// <param name="method"></param>
        /// <param name="message"></param>
        private void LogText(string method, string message)
        {
            TL?.LogMessage(method, message);
        }

        /// <summary>
        /// Log an OK message
        ///    </summary>
        ///    <param name="method">Calling method name</param>
        /// <param name = "message" > Message to log</param>
        private void LogOk(string method, string message, string contextMessage)
        {
            if (settings.AlpacaConfiguration.ProtocolShowSuccessResponses)
            {
                LogMessage(method, TestOutcome.OK, message, contextMessage);
            }
            else
            {
                LogMessage(method, TestOutcome.OK, message);
            }

        }

        /// <summary>
        /// Log an issue message
        /// </summary>
        /// <param name="method">    Calling method name</param>
        /// <param name = "message" > Message to log</param>
        private void LogIssue(string method, string message, string contextMessage)
        {
            LogMessage(method, TestOutcome.Issue, message, contextMessage);
            issueMessages.Add($"{method} ==> {message}\r\n  Response: {contextMessage}\r\n");
        }

        /// <summary>
        /// Log an issue message
        /// </summary>
        /// <param name="method">    Calling method name</param>
        /// <param name = "message" > Message to log</param>
        private void LogError(string method, string message, string contextMessage)
        {
            LogMessage(method, TestOutcome.Error, message, contextMessage);
            errorMessages.Add($"{method} ==> {message}\r\n  Response: {contextMessage}\r\n");
        }

        /// <summary>
        /// Log an information message
        /// </summary>
        /// <param name="method"> Calling method name</param>
        ///<param name = "message" > Message to log</param>
        private void LogInformation(string method, string message, string contextMessage)
        {
            LogMessage(method, TestOutcome.Info, message, contextMessage);
            informationMessages.Add($"{method} ==> {message} {(string.IsNullOrEmpty(contextMessage) ? "" : $"\r\n  Response: {contextMessage}")}\r\n"); //
        }

        /// <summary>
        /// Log a text line without additional formatting
        /// </summary>
        /// <param name="message"></param>
        private void LogLine(string message)
        {
            TL?.LogMessage(message, "");
        }

        /// <summary>
        /// Add a blank line to the log
        ///    </summary>
        private void LogBlankLine()
        {
            TL?.LogMessage("", "");
        }

        /// <summary>
        /// Set the status message
        ///    </summary>
        ///    <param name="message"></param>
        private void SetStatus(string message)
        {
            TL?.SetStatusMessage(message);
        }

        #endregion

    }
}