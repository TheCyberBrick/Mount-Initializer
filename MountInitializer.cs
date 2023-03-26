using ASCOM.DeviceInterface;
using ASCOM.DriverAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

class MountInitializer
{
    public static int Main(string[] args)
    {
        string GetArgument(string option) => args.SkipWhile(i => !i.Equals(option)).Skip(1).Take(1).FirstOrDefault();

        var condition = args.Contains("--condition") ? GetArgument("--condition") : null;
        var telescopeId = args.Contains("--telescope") ? GetArgument("--telescope") : null;
        var timeout = args.Contains("--timeout") ? int.TryParse(GetArgument("--timeout"), out var t) ? t : 60 : 60;
        var silent = args.Contains("--silent");
        var status = args.Contains("--status");
        var unpark = args.Contains("--unpark");
        var stopTracking = args.Contains("--stopTracking");
        var check = args.Contains("--check");

        if (condition == null)
        {
            Console.Error.WriteLine("No condition specified");
            return 2;
        }

        try
        {
            // Let user choose telescope if not yet specified
            telescopeId = telescopeId ?? Telescope.Choose("ASCOM.Simulator.Telescope");

            Telescope telescope = new Telescope(telescopeId);

            bool conditionMet = false;

            try
            {
                if (!silent)
                {
                    Console.WriteLine("Connecting to mount: " + telescopeId);
                }

                // Connect to mount
                try
                {
                    telescope.Connected = true;
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed connecting to mount", ex);
                }

                conditionMet = condition == "always" || (condition == "unknownSideOfPier" && telescope.SideOfPier == PierSide.pierUnknown);

                if (!check)
                {
                    if (conditionMet)
                    {
                        if (!silent)
                        {
                            Console.WriteLine("Initializing mount");
                        }

                        // Ensure mount supports syncing
                        if (!telescope.CanSync)
                        {
                            throw new Exception("Mount cannot sync");
                        }

                        // Ensure mount is unparked
                        if (unpark)
                        {
                            if (!telescope.CanUnpark)
                            {
                                throw new Exception("Mount cannot unpark");
                            }

                            if (!silent)
                            {
                                Console.WriteLine("Unparking...");
                            }

                            telescope.Unpark();
                        }
                        else if (telescope.AtPark)
                        {
                            throw new Exception("Mount is parked");
                        }

                        // Counter-weights-down position in RA and DEC coordinates
                        var cwdRightAscension = (telescope.SiderealTime + 6.0) % 24.0;
                        var cwdDeclination = 89.999999;

                        var syncTime = DateTime.Now;

                        if (!silent)
                        {
                            Console.WriteLine("Current LST: " + telescope.SiderealTime);
                            Console.WriteLine("Current declination: " + telescope.Declination + ", expected: " + cwdDeclination);
                            Console.WriteLine("Current right ascension: " + telescope.RightAscension + ", expected: " + cwdRightAscension);
                            Console.WriteLine("Syncing...");
                        }

                        // Sync to CWD coordinates
                        try
                        {
                            bool prevTracking = telescope.Tracking;

                            // Must be tracking to sync
                            telescope.Tracking = true;
                            telescope.SyncToCoordinates(cwdRightAscension, cwdDeclination);

                            telescope.Tracking = stopTracking ? false : prevTracking;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Failed syncing mount", ex);
                        }

                        var timeoutStart = DateTime.Now;
                        while (true)
                        {
                            bool retry = (DateTime.Now - timeoutStart).Seconds < timeout;

                            Thread.Sleep(TimeSpan.FromSeconds(1));

                            if (!silent)
                            {
                                Console.WriteLine("Checking status...");
                            }

                            // Ensure mount tracking state is correct
                            if (stopTracking && telescope.Tracking)
                            {
                                if (!retry)
                                {
                                    throw new Exception("Mount is still tracking");
                                }
                                else
                                {
                                    if (!silent)
                                    {
                                        Console.WriteLine("Tracking not ready");
                                    }
                                    continue;
                                }
                            }

                            // Ensure side of pier is correct
                            if (telescope.SideOfPier == PierSide.pierUnknown)
                            {
                                if (!retry)
                                {
                                    throw new Exception("Mount is still reporting unknown side of pier");
                                }
                                else
                                {
                                    if (!silent)
                                    {
                                        Console.WriteLine("Side of pier not ready");
                                    }
                                    continue;
                                }
                            }

                            // Ensure declination is correct (+-0.01°)
                            var currentDec = telescope.Declination;
                            if (currentDec < 89.99 || currentDec > 90.0)
                            {
                                if (!retry)
                                {
                                    throw new Exception("Unexpected declination: " + currentDec + ", expected: " + cwdDeclination);
                                }
                                else
                                {
                                    if (!silent)
                                    {
                                        Console.WriteLine("Declination not ready");
                                    }
                                    continue;
                                }
                            }

                            // Ensure right ascension is correct (+-3s)
                            var timeSinceSync = DateTime.Now - syncTime;
                            var expectedRightAscension = cwdRightAscension + timeSinceSync.Seconds / 3600.0;
                            var currentRightAscension = telescope.RightAscension;
                            var diff = Math.Abs(currentRightAscension - expectedRightAscension) % 24.0f;
                            diff = diff > 12.0 ? 24.0 - diff : diff;
                            if (diff > 3.0 / 3600.0)
                            {
                                if (!retry)
                                {
                                    throw new Exception("Unexpected right ascension: " + currentRightAscension + ", expected: " + expectedRightAscension);
                                }
                                else
                                {
                                    if (!silent)
                                    {
                                        Console.WriteLine("Right ascension not ready");
                                    }
                                    continue;
                                }
                            }

                            // Successfully initialized

                            if (!silent)
                            {
                                Console.WriteLine("Mount initialized");
                            }

                            break;
                        }
                    }
                    else if (!silent)
                    {
                        Console.WriteLine("Mount already initialized");
                    }
                }
            }
            finally
            {
                // Always try to disconnect
                try
                {
                    telescope.Connected = false;
                }
                catch (Exception)
                {
                }

                telescope.Dispose();
            }


            if (check)
            {
                if (status)
                {
                    Console.WriteLine(conditionMet ? "OK" : "FAILURE");
                }
                return conditionMet ? 0 : 1;
            }
            else
            {
                if (status)
                {
                    Console.WriteLine("OK");
                }
                return 0;
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(ex.Message + ":");
                    Console.Error.WriteLine(ex.InnerException.Message);
                }
                else
                {
                    Console.Error.WriteLine(ex.Message);
                }
            }
        }

        if (status)
        {
            Console.WriteLine("FAILURE");
        }
        return 1;
    }
}
