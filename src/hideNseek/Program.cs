using System;
using System.Collections.Generic;
using StereoKit;


namespace hideNseek
{
   
    class Program
    {
        
        
        static void Main(string[] args)
        {

            // Initialize StereoKit
            SKSettings settings = new SKSettings
            {
                appName = "hideNseek",
                assetsFolder = "Assets",
            };
            if (!SK.Initialize(settings))
                Environment.Exit(1);

            Game game = new Game();

            // Core application loop
            while (SK.Step(() =>
            {

                if (!game.Step())
                {
                    SK.Quit();
                }
                
            })) ;
            SK.Shutdown();
        }
    }
}
