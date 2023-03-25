using ASCOM.DeviceInterface;
using ASCOM.DriverAccess;
using System;
using System.Linq;
using System.Threading;

class MountInitializer
{
    public static int Main(string[] args)
    {
        string GetArgument(string option) => args.SkipWhile(i => !i.Equals(option)).Skip(1).Take(1).FirstOrDefault();

        var telescopeId = args.Contains("--telescope") ? GetArgument("--telescope") : null;
        var timeout = args.Contains("--timeout") ? int.TryParse(GetArgument("--timeout"), out var t) ? t : 60 : 60;
        var silent = args.Contains("--silent");
        var force = args.Contains("--force");

        try
        {
            // Let user choose telescope if not yet specified
            telescopeId = telescopeId ?? Telescope.Choose("ASCOM.Simulator.Telescope");

            Telescope telescope = new Telescope(telescopeId);

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

                // Ensure mount supports syncing
                if (!telescope.CanSync)
                {
                    throw new Exception("Mount cannot sync");
                }

                // Only initialize if pier unknown
                if (force || telescope.SideOfPier == PierSide.pierUnknown)
                {
                    if (!silent)
                    {
                        Console.WriteLine("Initializing mount");
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
                        // Must be tracking to sync
                        telescope.Tracking = true;
                        telescope.SyncToCoordinates(cwdRightAscension, cwdDeclination);
                        telescope.Tracking = false;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Failed syncing mount", ex);
                    }

                    int time = 0;
                    while (true)
                    {
                        bool retry = time++ < timeout;

                        Thread.Sleep(TimeSpan.FromSeconds(1));

                        if (!silent)
                        {
                            Console.WriteLine("Checking status...");
                        }

                        // Wait for side of pier to no longer be unknown
                        if (telescope.SideOfPier != PierSide.pierUnknown)
                        {
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
                        else if (!retry)
                        {
                            throw new Exception("Mount is still reporting unknown side of pier");
                        }
                        else if (!silent)
                        {
                            Console.WriteLine("Side of pier not ready");
                        }
                    }
                }
                else if (!silent)
                {
                    Console.WriteLine("Mount already initialized");
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

            Console.WriteLine("OK");
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                if (ex.InnerException != null)
                {
                    Console.WriteLine(ex.Message + ":");
                    Console.WriteLine(ex.InnerException.Message);
                }
                else
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        Console.WriteLine("FAIL");
        return -1;
    }
}
