using System;
using System.Collections.Generic;
using StereoKit;

namespace hideNseek
{
    class Target
    {
        public Pose pose;
        public Model model;
        public float scale;
        public string name;
        public Boolean isDetected;
        public Boolean isHandled;
        
        public Target (Pose pose, Model target, float diameter, string name)
        {
            this.pose = new Pose(pose.position,pose.orientation);
            this.model = target;
            this.scale = diameter;
            this.name = name;
        }
        public Boolean isTargetDetected( Material hide, Material seen)
        {
            // draw the target and detect if the target is seen
            //

            Boolean detected = false;
            Material mat = hide;
            Matrix targetTransform = this.pose.ToMatrix(scale); // move and scale

            if (Input.EyesTracked.IsActive())
            {
                //UI.Label("Eye tracking active");
                // Intersect the eye Ray with the objects
                Sphere zone = new Sphere(this.pose.position, this.scale);
                if (Input.Eyes.Ray.Intersect(zone, out Vec3 at))
                {
                    detected = true;
                    mat = seen; // change target material
                    Default.MeshSphere.Draw(Default.Material, Matrix.TS(at, .02f));
                }
            }
           
            // set target Material
            this.model.RootNode.Material = mat;
            // draw target
            Bounds scaledBounds = new Bounds(this.model.Bounds.center, this.model.Bounds.dimensions * scale);
            this.isHandled = UI.Handle(this.name, ref this.pose, scaledBounds);
            this.model.Draw(targetTransform);
            return detected;
        }
    }
    class Game
    {
        private  List<Target> targetList = new List<Target>();
        private float targetDiameter = 0.1f;
        private Material targetMaterial, seenMaterial, invisibleMaterial;
        private Pose windowAdminPose;
        private Pose windowUserPose;
        private string mode = "admin"; // or "user"
        private Boolean useSound = false;
        private Boolean useIndicator = false;
        private Sound foundSound, winSound;
        private Target currentTarget;

        public Game() {
            // Create assets used by the game
            
            targetMaterial = Default.Material.Copy(); //matAlphaBlend
            targetMaterial.Transparency = Transparency.Blend;
            targetMaterial.DepthWrite = false;
            targetMaterial[MatParamName.ColorTint] = new Color(.4f, 1, 0.4f, 0.4f);

            // seenMaterial = Default.Material.Copy(); //matAlphaBlend
            // seenMaterial.Transparency = Transparency.Blend;
            // seenMaterial.DepthWrite = false;
            // seenMaterial[MatParamName.ColorTint] = new Color(1f, 0, 0.4f, 0.8f);
            // seenMaterial.Wireframe = true;

            seenMaterial = Default.Material.Copy(); //matAlphaBlend
            seenMaterial.Transparency = Transparency.Blend;
            seenMaterial.DepthWrite = false;
            seenMaterial[MatParamName.ColorTint] = new Color(.2f, 1, 0.2f, 0.8f);

            invisibleMaterial = Default.Material.Copy(); //matAlphaBlend
            invisibleMaterial.Transparency = Transparency.Blend;
            invisibleMaterial.DepthWrite = false;
            invisibleMaterial[MatParamName.ColorTint] = new Color(0, 0, 0, 1f);
            invisibleMaterial.Wireframe = true;

            windowAdminPose = new Pose(-.2f, 0, -0.5f, Quat.LookAt(new Vec3(-.2f, 0, -0.5f),Input.Head.position,Vec3.Up));
            windowUserPose = new Pose(+.4f, 0, -0.5f, Quat.LookAt(new Vec3(-.2f, 0, -0.5f), Input.Head.position, Vec3.Up));
            foundSound = Sound.FromFile("sound_success.wav");
            winSound = Sound.FromFile("applause.wav");
            adminMode();
            // Pose targetPose = new Pose(0, 0, -0.5f, Quat.Identity);
            //addTarget(targetPose, targetDiameter);
        }


        public void  clear()
        {
            targetList.Clear();
        }
        public void reset()
        {
            foreach (var target in this.targetList)
            {
                target.isDetected = false;
            }
        }
        public void targetDetected(Target target)
        {
            target.isDetected = true;
        }
        public Boolean isFinished()
        {
            Boolean done = true;
            foreach (var target in this.targetList)
            {
                done = done && target.isDetected;
            }
            return done;
        }
        public void adminMode()
        {
            this.reset();
            currentTarget = null;
            mode = "admin";
        }
        public void userMode()
        {
            this.reset();
            mode = "user";
        }
        public void addTarget(Pose pose, float diameter)
        {
            Model target = Model.FromMesh(
                //Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
                Mesh.GenerateSphere(1.0f),
                targetMaterial);
            string name = "Target " + targetList.Count;
            Target t = new Target(pose,target,diameter,name);
            targetList.Add(t);
        }
        public Boolean Step()
        {
            Boolean running = true;
            // test each target
           
            foreach (var target in this.targetList)
            {
                if ("admin" == mode)
                {
                    if (target.isTargetDetected(targetMaterial, seenMaterial))
                    {
                        this.currentTarget = target;
                    };
                } else
                {
                    if (target.isTargetDetected(invisibleMaterial, invisibleMaterial))
                    {
                        
                        if (target.isDetected == false)
                        {
                            
                            targetDetected(target);
                            if (isFinished())
                            {
                                this.winSound.Play(target.pose.position);
                            } else
                            {
                                this.foundSound.Play(target.pose.position);
                            }
                        }
                    };
                }
            }
            // Target config UI panel
            if (("admin"==mode) && (currentTarget != null) && !currentTarget.isHandled)
            {
                // display the target panel config
               
                Vec3 toHead = (Input.Head.position - currentTarget.pose.position).Normalized;
                Pose infoPose = new Pose(currentTarget.pose.position + (toHead * 0.15f) - (Vec3.Up * 0.10f), Quat.LookDir(toHead));

                UI.WindowBegin("Target configuration", ref infoPose, new Vec2(20, 0) * U.cm, UIWin.Normal);
                UI.Label("size");
                UI.SameLine();
                UI.HSlider("size", ref currentTarget.scale, 0.05f, .3f, 0, 10 * U.cm);
                UI.WindowEnd();

            }

            bool showHeader = true;

            Sprite powerSprite = Sprite.FromFile("power.png", SpriteType.Single);

            // User panel 
            // no draggable in user mode

            if ("admin" == mode) 
            {
                UI.WindowBegin("Hunting board", ref this.windowUserPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                UI.Text("You are in design mode. This is a preview of the player board. Move it at a convenient place for the player.");
            } else
            {
                Pose forgetPose = new Pose(this.windowUserPose.position, this.windowUserPose.orientation);
                UI.WindowBegin("Hunting board", ref forgetPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                // pose is not save so handle has no effect 
            }
            if (isFinished())
            {
                UI.Text("Well done, you find all the hidden gems in this romm !");
                if (UI.Button("Re-Play"))
                {
                    reset();
                }
            }
            else
            {
                UI.Text("There are some interresting things in this room. Look around. Could you spot them ?");

                foreach (var target in this.targetList)
                {
                    UI.Label(target.name + (target.isDetected ? " found" : "..."));
                }
            }

            UI.HSeparator();
            UI.Label("Need some help?");
            UI.Toggle("Add sound", ref useSound);
            UI.Toggle("Add indicator", ref useIndicator);
            UI.HSeparator();
            
            
            if (UI.ButtonRound("Abort mission", powerSprite))
            {
                adminMode();
            }
            UI.SameLine(); UI.Label("Abort mission");
            
            UI.WindowEnd();

            if (mode == "admin")
            {
                UI.WindowBegin("Game Admin", ref this.windowAdminPose, new Vec2(20, 0) * U.cm, UIWin.Normal);
                UI.Text("You are in design mode. Find interresting objects in the surrounding and place 'target' on it. ");

                
                if (UI.Button("Add target"))
                {
                    Pose targetPose = new Pose(0, 0, -0.5f, Quat.Identity);
                    // TO DO : create a target just next to the Admin panel 
                    addTarget(targetPose, targetDiameter);
                }
                if (UI.Button("Start game"))
                {
                    userMode();
                }
                if (UI.Button("Test sound"))
                {
                    this.foundSound.Play(Vec3.Zero);
                }
                if (UI.ButtonRound("Exit", powerSprite))
                    running = false;
                UI.WindowEnd();
            }

            

            return running;
        }
    }
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
