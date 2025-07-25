using System.Runtime.InteropServices;
using System.Text;

namespace OKDPlayer
{
    internal class Program
    {
        private static int transposeKey = 0;
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("OKD Player - DAM OKD File Player");
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: OKDPlayer <path_to_okd_file> <midi device id>...");
                return;
            }
            Console.WriteLine($"Loading OKD file: {args[0]}");
            OKD okd = new OKD();

            okd.LoadFromFile(args[0], File.Exists("key.bin") ? "key.bin" : null);

            Console.WriteLine($"OKD file loaded successfully. PTrack Count :{okd.PTracks.Length}");

            //split input by space 
            string[] inputParts = null;

            if (args.Length > 2)
            {
                inputParts = args.Skip(1).ToArray();
            }
            else
            {
                // Get available MIDI output devices
                string[] midiDevs = MIDIDevice.GetOutputDeviceNames().ToArray();
                if (midiDevs.Length == 0)
                {
                    Console.WriteLine("No MIDI output devices found. Please connect a MIDI device and try again.");
                    return;
                }

                //print available MIDI devices
                Console.WriteLine("Available MIDI Output Devices:");
                for (int i = 0; i < midiDevs.Length; i++)
                {
                    Console.WriteLine($"{i}: {midiDevs[i]}");
                }

                Console.WriteLine($"Select MIDI Output Device by number. This OKD file needs {okd.PTracks.Length} MIDI Ports.");
                string input = Console.ReadLine();
                inputParts = input.Split(' '); 
            }

           
            
            if (inputParts.Length < okd.PTracks.Length)
            {
                Console.WriteLine($"You must select {okd.PTracks.Length} MIDI devices.");
                return;
            }
            //parse input and get MIDI devices by index
            if (!inputParts.All(part => int.TryParse(part, out _)))
            {
                Console.WriteLine("Invalid input. Please enter valid MIDI device indices separated by spaces.");
                return;
            }
            


            List<IMIDIDevice> midiDevsList = new List<IMIDIDevice>();
            foreach (var part in inputParts)
            {
                if (int.TryParse(part, out int index) && index >= 0)
                {
                    IMIDIDevice dev = null;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        dev = new WinMIDIDevice();
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        dev = new LinuxMIDIDevice();
                    bool res = dev.Open(index);
                    if (!res)
                    {
                        Console.WriteLine($"Failed to open MIDI device at index {index}. Please check the device and try again.");
                        return;
                    }
                    midiDevsList.Add(dev);
                }
                else
                {
                    Console.WriteLine($"Invalid MIDI device index: {index}. Please select a valid index.");
                    return;
                }
            }


            okd.SetMIDIDevice(midiDevsList.ToArray());


            Console.WriteLine("Resetting TG Devices...");
            okd.ResetTGDevices(true);
            okd.AdjustTGVolume();

            //Masterclock setup
            MasterClock masterClock = new MasterClock();

            OKDPlayback[] playbacks = okd.GetPTrackPlaybacks();
           

            Console.WriteLine("Starting playback... Press Enter to stop playback, Up/Down arrows to change speed, PageUp/PageDown to transpose.");
            foreach (var playback in playbacks)
            {
                playback.Play(masterClock);
                //break;
            }
            masterClock.Start();

            //wait for playback to finish
            while (playbacks.Any(p => p.IsPlaying))
            {
                //check key input
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Enter)
                    {
                        //Stop all playbacks
                        foreach (var playback in playbacks.Where(p => p.IsPlaying))
                        {
                            Console.WriteLine($"Stopping playback...");
                            playback.Stop();
                            masterClock.Stop();
                            playback.MidiDevice.StopAllSound();
                            playback.MidiDevice.Device.Close();
                        }
                        break;
                    }
                    else if (key == ConsoleKey.UpArrow)
                    {
                        //speed up
                        
                        masterClock.SpeedRatio += 0.1f; // Increase speed by 10%
                        Console.WriteLine($"Playback speed set to {masterClock.SpeedRatio}");
                    }
                    else if (key == ConsoleKey.DownArrow)
                    {
                        //slow down
                        
                        if (masterClock.SpeedRatio <= 0.1f)
                            continue; // Don't allow speed to go below 0.1
                        masterClock.SpeedRatio -= 0.1f; // Decrease speed by 10%, but not below 0.1
                        Console.WriteLine($"Playback speed set to {masterClock.SpeedRatio}");
                    }
                    else if(key == ConsoleKey.LeftArrow)
                    {
                        Console.WriteLine($"Seeking backward 10 seconds.");
                        masterClock.Seek(-10000);
                        foreach (var playback in playbacks.Where(p => p.IsPlaying))
                        {
                            playback.MidiDevice.StopAllSound();
                        }
                    }
                    else if (key == ConsoleKey.RightArrow)
                    {
                        Console.WriteLine($"Seeking forward 10 seconds.");
                        masterClock.Seek(10000);
                        foreach (var playback in playbacks.Where(p => p.IsPlaying))
                        {
                            playback.MidiDevice.StopAllSound();
                        }
                    }
                    else if(key == ConsoleKey.PageUp)
                    {
                        transposeKey++;
                        okd.Transpose(transposeKey);
                        Console.WriteLine($"Transposing up: {transposeKey} semitones");
                    }
                    else if (key == ConsoleKey.PageDown)
                    {
                        transposeKey--;
                        okd.Transpose(transposeKey);
                        Console.WriteLine($"Transposing down: {transposeKey} semitones");
                    }

                }
                int totalSeconds = (int)(masterClock.CurrentVirtualTime / 1000);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;

                Console.WriteLine($"-> Playing.. {minutes:D2}:{seconds:D2}");
                Console.SetCursorPosition(0, Console.CursorTop - 1); 
                Thread.Sleep(100);
            }

           
        }
    }
}
