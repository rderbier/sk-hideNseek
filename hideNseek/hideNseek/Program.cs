using System;
using System.Collections.Generic;
using StereoKit;

namespace hideNseek
{
    class Target
    {
        public Pose pose;
        public Target (Pose pose)
        {
            this.pose = pose;
        }
    }
    class Game
    {
        private  List<Target> targetList = new List<Target>();
        public Game() { }

        public void  reset()
        {
            targetList.Clear();
        }
        public void addTarget(Pose pose)
        {
            Target t = new Target(pose);
            targetList.Add(t);
        }
    }
    class Program
    {
        const float targetDiameter = 0.1f;
        static Boolean isTargetDetected(ref Pose targetPose, Model target, Material hide, Material seen)
        {
            Boolean detected = false;
            Material mat = hide;
            Matrix targetTransform = targetPose.ToMatrix();
           
            if (Input.EyesTracked.IsActive())
            {
                //UI.Label("Eye tracking active");
                // Intersect the eye Ray with the objects
                Sphere zone = new Sphere(targetPose.position, targetDiameter);
                if (Input.Eyes.Ray.Intersect(zone, out Vec3 at))
                {
                    detected = true;
                    mat = seen; // change target material
                    Default.MeshSphere.Draw(Default.Material, Matrix.TS(at, .02f));
                   // Hierarchy.Pop();
                   // UI.Label("target in view at " + at.v.ToString("F2"));
                }
            }
            else
            {
               // UI.Label("no eye tracking");
            }
            // set target Material
            target.SetMaterial(0,mat);
            // draw target
            UI.Handle("Target", ref targetPose, target.Bounds);
            target.Draw(targetTransform);
            return detected;
        }
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

            
            // Create assets used by the app
            Pose targetPose = new Pose(0, 0, -0.5f, Quat.Identity);
            Material targetMaterial = Default.Material.Copy(); //matAlphaBlend
            targetMaterial.Transparency = Transparency.Blend;
            targetMaterial.DepthWrite = false;
            targetMaterial[MatParamName.ColorTint] = new Color(.4f, 1, 0.4f, 0.5f);
            Material seenMaterial = Default.Material.Copy(); //matAlphaBlend
            seenMaterial.Transparency = Transparency.Blend;
            seenMaterial.DepthWrite = false;
            seenMaterial[MatParamName.ColorTint] = new Color(1f, 0, 0.4f, 0.8f);
            
            
            
            Model target = Model.FromMesh(
                //Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
                Mesh.GenerateSphere(targetDiameter),
                targetMaterial);



            // UI panel
            Pose windowPose = new Pose(-.4f, 0, -0.5f, Quat.LookDir(1, 0, 1));

            bool showHeader = true;
            float slider = 0.5f;

            Sprite powerSprite = Sprite.FromFile("power.png", SpriteType.Single);

            // Core application loop
            while (SK.Step(() =>
            {







                Boolean targetDetected = isTargetDetected(ref targetPose, target, targetMaterial, seenMaterial);
               
                UI.WindowBegin("Window", ref windowPose, new Vec2(20, 0) * U.cm, showHeader ? UIWin.Normal : UIWin.Body);
                if (targetDetected)
                {
                    UI.Label("target at " + targetPose.position.v.ToString("F2"));
                }
                if (UI.ButtonRound("Exit", powerSprite))
                    SK.Quit();
                UI.WindowEnd();
            })) ;
            SK.Shutdown();
        }
    }
}
