using Json.Net;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;

namespace CSGO_Simple_Glow
{
    class Program
    {
        static void Main()
        {
            //Kredits to Kye#5000
            Console.Title = $"CS:GO Simple Glow v1 - {Utils.RandomString(5)}";
            //Thread.CurrentThread.Priority = ThreadPriority.Highest;
            //Download our offsets from hazedumper
            Hazedumper.Root offsets = JsonNet.Deserialize<Hazedumper.Root>(new WebClient().DownloadString("https://raw.githubusercontent.com/frk1/hazedumper/master/csgo.min.json"));
            TimeSpan updatedtime = DateTime.Now - DateTimeOffset.FromUnixTimeSeconds(offsets.timestamp).DateTime.ToLocalTime();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Fetched offsets from hazedumper, last updated {Utils.GetReadableTimespan(updatedtime)} ago");
            start: Process[] csgoproc = Process.GetProcessesByName("csgo");
            if (csgoproc.Length > 0)
            {
                Memory.ProcessHandle = Memory.OpenProcess(0x0008 | 0x0010 | 0x0020, false, csgoproc[0].Id);
                Console.WriteLine($"Opened Process csgo.exe ({csgoproc[0].Id})");
            }
            else
            {
                Console.WriteLine("Waiting for csgo to start...");
                while (true)
                {
                    Process[] trygetcsgo = Process.GetProcessesByName("csgo");
                    if (trygetcsgo.Length > 0)
                    {
                        Thread.Sleep(3000);//Wait for client.dll & engine.dll to be loaded into csgo.
                        goto start;
                    }
                }
            }
            //Get the base addy for client.dll & engine.dll inside our csgo.exe
            int clientdll = GetModuleBaseAddress(csgoproc[0], "client.dll");
            int enginedll = GetModuleBaseAddress(csgoproc[0], "engine.dll");

            //Our general settings
            GlowSettingsStruct glowSettingsStruct = new GlowSettingsStruct() { renderOccluded = true, renderUnoccluded = false };
            //Get our local player
            while (true)
            {
                //Keep reading our local player coz incase teams change and stuff
                int LocalPlayer = Memory.ReadMemory<int>(clientdll + offsets.signatures.dwLocalPlayer);
                int clientstate = Memory.ReadMemory<int>(enginedll + offsets.signatures.dwClientState);
                int MaxPlayerCount = Memory.ReadMemory<int>(clientstate + offsets.signatures.dwClientState_MaxPlayer);
                //Basically sleep the hack if ur in menu and such
                if (MaxPlayerCount < 1)
                {
                    Thread.Sleep(500);
                    continue;
                }
                //Get our glow object
                int glowObject = Memory.ReadMemory<int>(clientdll + offsets.signatures.dwGlowObjectManager);
                //Get our team number
                int myTeam = Memory.ReadMemory<int>(LocalPlayer + offsets.netvars.m_iTeamNum);

                for (int i = 0; i < MaxPlayerCount; i++)
                {
                    //The current entity
                    int entity = Memory.ReadMemory<int>(clientdll + offsets.signatures.dwEntityList + i * 0x10);
                    bool bDormant = Memory.ReadMemory<bool>(entity + offsets.signatures.m_bDormant);
                    if (!bDormant)
                    {
                        int glowIndex = Memory.ReadMemory<int>(entity + offsets.netvars.m_iGlowIndex);
                        int entityTeam = Memory.ReadMemory<int>(entity + offsets.netvars.m_iTeamNum);
                        if (myTeam == entityTeam)
                        {
                            GlowColorStruct TeamGlow = new GlowColorStruct() { red = 0, green = 1, blue = 0, alpha = 0.1f };

                            Memory.WriteMemory<GlowColorStruct>(glowObject + (glowIndex * 0x38) + 0x8, TeamGlow);

                            rgba clrRender_t = new rgba
                            {
                                //*255 idea from: https://stackoverflow.com/a/46575472/12897035
                                r = (byte)Math.Round(TeamGlow.red * 255.0),
                                g = (byte)Math.Round(TeamGlow.green * 255.0),
                                b = (byte)Math.Round(TeamGlow.blue * 255.0),
                                a = (byte)Math.Round(TeamGlow.alpha * 255.0)
                            };
                            Memory.WriteMemory<GlowColorStruct>(entity + offsets.netvars.m_clrRender, clrRender_t);
                        }
                        else
                        {
                            GlowColorStruct EnemyGlow = new GlowColorStruct() { red = 1, green = 0, blue = 0, alpha = 0.35f };
                            if (Memory.ReadMemory<bool>(entity + offsets.netvars.m_bIsDefusing))
                                EnemyGlow = new GlowColorStruct() { red = 255, green = 255, blue = 255, alpha = 0.1f };
                            else
                                Memory.WriteMemory<GlowColorStruct>(glowObject + (glowIndex * 0x38) + 0x8, EnemyGlow);

                            rgba clrRender_t = new rgba
                            {
                                r = (byte)Math.Round(EnemyGlow.red * 255.0),
                                g = (byte)Math.Round(EnemyGlow.green * 255.0),
                                b = (byte)Math.Round(EnemyGlow.blue * 255.0),
                                a = (byte)Math.Round(EnemyGlow.alpha * 255.0)
                            };
                            Memory.WriteMemory<GlowColorStruct>(entity + offsets.netvars.m_clrRender, clrRender_t);
                            //Our teammates are shown on map so we only have to write the radar to our enemies. (Basically this: https://youtu.be/5VOkRJk1GVg)
                            Memory.WriteMemory<bool>(entity + offsets.netvars.m_bSpotted, true);
                        }
                        Memory.WriteMemory<GlowSettingsStruct>(glowObject + ((glowIndex * 0x38) + 0x28), glowSettingsStruct);
                    }
                }
                Thread.Sleep(1);//Change this if ur cpu can't handle it :pepeclown:
            }
        }

        public struct GlowColorStruct
        {
            public float red { get; set; }//0x8
            public float green { get; set; }//0xC
            public float blue { get; set; }//0x10
            public float alpha { get; set; }//0x14
        }

        public struct rgba//Overlay player color or "chams"
        {
            public byte r;
            public byte g;
            public byte b;
            public byte a;
        }

        public struct GlowSettingsStruct
        {
            public bool renderOccluded { get; set; }
            public bool renderUnoccluded { get; set; }
        }

        public static int GetModuleBaseAddress(Process process, string moduleName)
        {
            return (int)process.Modules.Cast<ProcessModule>().SingleOrDefault(m => string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)).BaseAddress;
        }

    }
}
