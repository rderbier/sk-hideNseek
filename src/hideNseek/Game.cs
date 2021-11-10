using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StereoKit;
using Windows.Storage;
using Windows.Storage.Streams;

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
        public Sound memo;
        public Boolean isSelected; // true for the currently selected target
        public float gazeTime; // howlong this target has the gaze

        public Target(Pose pose, Model target, float diameter, string name)
        {
            this.pose = new Pose(pose.position, pose.orientation);
            this.model = target;
            this.scale = diameter;
            this.name = name;
            this.memo = null;
            this.gazeTime = 0f;
            tryRestore();
        }
        public Boolean isTargetDetected(float gazeDuration, Material hide, Material seen, Material selected, float distance = 2.0f, Boolean gazeIndicator = false)
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
                // user must be close enough
                if (Vec3.Distance(Input.Head.position, this.pose.position) < distance)
                {
                    Sphere zone = new Sphere(this.pose.position, this.scale);
                    if (Input.Eyes.Ray.Intersect(zone, out Vec3 at))
                    {
                        if (gazeIndicator)
                        {
                            Default.MeshSphere.Draw(Default.Material, Matrix.TS(at, .02f));
                        }
                        gazeTime += Time.Elapsedf;
                        if (gazeTime > gazeDuration)
                        {
                            detected = true;
                            mat = seen; // change target material

                        }
                    }
                    else
                    {
                        gazeTime = 0f;
                    }
                }
            }
            if (isSelected) { mat = selected; }

            // set target Material
            this.model.RootNode.Material = mat;
            // draw target
            Bounds scaledBounds = new Bounds(this.model.Bounds.center, this.model.Bounds.dimensions * scale);
            this.isHandled = UI.Handle(this.name, ref this.pose, scaledBounds);
            this.model.Draw(targetTransform);
            return detected;
        }
        public async void saveAsync()
        {
            //save target info to file and to Anchor.
            if (this.memo != null)
            {
                try
                {
                    SoundInst s = this.memo.Play(Vec3.Zero);
                    s.Stop();

                    float[] floatBuffer = new float[this.memo.TotalSamples];
                    this.memo.ReadSamples(ref floatBuffer);
                    byte[] byteBuffer = new byte[floatBuffer.Length * 4];
                    System.Buffer.BlockCopy(floatBuffer, 0, byteBuffer, 0, byteBuffer.Length);
                    Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    Windows.Storage.StorageFile memoFile =
                        await storageFolder.CreateFileAsync(
                            "memo-" + this.name,
                            Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    if (memoFile != null)
                    {
                        await Windows.Storage.FileIO.WriteBytesAsync(memoFile, byteBuffer);
                    }
                } catch (Exception ex)
                {
                    // ignore file errors for now !
                }
            }

        }
        public async void tryRestore()
        {
            // try to get info from file 
            // restore memo
            try
            {
                Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                Windows.Storage.StorageFile dataFile = await storageFolder.GetFileAsync("memo-" + this.name);
                if (dataFile != null)
                {
                    IBuffer buffer = await FileIO.ReadBufferAsync(dataFile);

                    // Use a dataReader object to read from the buffer
                    using (DataReader dataReader = DataReader.FromBuffer(buffer))
                    {
                        byte[] byteBuffer = new byte[buffer.Length];
                         dataReader.ReadBytes(byteBuffer);
                        var floatArray = new float[byteBuffer.Length / 4];
                        System.Buffer.BlockCopy(byteBuffer, 0, floatArray, 0, byteBuffer.Length);
                        this.memo = Sound.CreateStream((float)floatArray.Length / 48000f); // in seconds
                        this.memo.WriteSamples(floatArray);
                    }
                }
            } catch (Exception e)
            {
                // ignore errors
            }
        }
    }
    class Game
    {
        private  const Int32  MAX_SOUND_LENGTH = 48000 * 5;
        private  const String ADMIN_MODE = "admin";
        private const String USER_MODE = "user";
        private const float TARGET_MIN_DISTANCE = 2.0f;

        private List<Target> targetList = new List<Target>();
        private float targetDiameter = 0.1f;
        private Material targetMaterial, seenMaterial, invisibleMaterial, selectedMaterial, hintMaterial;
        private Pose windowAdminPose;
        private Pose windowUserPose;
        private string mode = Game.ADMIN_MODE; // or "user"
        private Boolean useSound = false;
        private Boolean useIndicator = false;
        private Boolean useCompass = false;
        private Sound foundSound, winSound, promptFind, promptNext, prompt, targetSound;
        private SoundInst playingSound;
        private String promptState;
        private Target currentTarget;
        private float[] micBuffer;
        private Single[] soundChunk;
        private Int32 micIndex;
        private Sound memo;
        private Sprite powerSprite, micSprite, trashSprite, speakerSprite, onSprite, offSprite;
        private Model compass;
        private Boolean playing = false;
        private Double huntingDuration = 0f;
        private int debugRound;
        private float debugTime;

        public Game()
        {
            
            // Create assets used by the game
            micBuffer = new float[MAX_SOUND_LENGTH]; // 5 seconds
            soundChunk = new Single[24000]; // 0.5 second max
            micIndex = 0;
            targetMaterial = Default.Material.Copy(); //matAlphaBlend
            targetMaterial.Transparency = Transparency.Blend;
            targetMaterial.DepthWrite = false;
            targetMaterial[MatParamName.ColorTint] = new Color(.3f, 1, 0.3f, 0.5f);
            targetMaterial.Wireframe = true;

            // seenMaterial = Default.Material.Copy(); //matAlphaBlend
            // seenMaterial.Transparency = Transparency.Blend;
            // seenMaterial.DepthWrite = false;
            // seenMaterial[MatParamName.ColorTint] = new Color(1f, 0, 0.4f, 0.8f);
            // seenMaterial.Wireframe = true;

            seenMaterial = Default.Material.Copy(); //matAlphaBlend
            seenMaterial.Transparency = Transparency.Blend;
            seenMaterial.DepthWrite = false;
            seenMaterial[MatParamName.ColorTint] = new Color(.3f, 1, 0.3f, 0.2f);

            selectedMaterial = seenMaterial.Copy();

            invisibleMaterial = Default.Material.Copy(); //matAlphaBlend
            invisibleMaterial.Transparency = Transparency.Blend;
            invisibleMaterial.DepthWrite = false;
            invisibleMaterial[MatParamName.ColorTint] = new Color(0, 0, 0, 1f);
            invisibleMaterial.Wireframe = true;

            hintMaterial = Default.Material.Copy(); //matAlphaBlend
            hintMaterial.Transparency = Transparency.Blend;
            hintMaterial.DepthWrite = false;
            hintMaterial[MatParamName.ColorTint] = new Color(1f, 1f, 1f, 0.1f);

            windowAdminPose = new Pose(-.2f, 0, -0.65f, Quat.LookAt(new Vec3(-.2f, 0, -0.5f), Input.Head.position, Vec3.Up));
            windowUserPose = new Pose(+.4f, 0, -0.65f, Quat.LookAt(new Vec3(+.4f, 0, -0.5f), Input.Head.position, Vec3.Up));
            foundSound = Sound.FromFile("sound_success.wav");
            winSound = Sound.FromFile("welldone.wav");
            promptFind = Sound.FromFile("tofind.wav");
            promptNext = Sound.FromFile("nextfind.wav");
            targetSound = Sound.FromFile("radar-sound.wav");

            Material compassMaterial = Material.Default.Copy();
            compass = Model.FromMesh(MeshUtils.createArrow(0.01f, 0.005f, 0.06f), compassMaterial);

            powerSprite = Sprite.FromFile("power.png", SpriteType.Single);
            micSprite = Sprite.FromFile("microphone.png", SpriteType.Single);
            speakerSprite = Sprite.FromFile("speaker.png", SpriteType.Single);
            trashSprite = Sprite.FromFile("trash.png", SpriteType.Single);
            onSprite = Sprite.FromFile("on.png", SpriteType.Single);
            offSprite = Sprite.FromFile("off.png", SpriteType.Single);
            adminMode();

            // init UI
            Microphone.Start();
            Microphone.Stop();

            // set UI scheme
            Color uiColor = Color.HSV(.83f, 0.33f, 1f, 0.8f);
            UI.ColorScheme = uiColor;

        }


        public void clear()
        {
            targetList.Clear();
        }
        public void startHunt()
        {
            reset();
            huntingDuration = 0f;
            playing = true;
            this.currentTarget = targetList[0];
            promptInstruction(promptFind);

        }
        public void promptInstruction(Sound s = null)
        {
            if (s != null)
            {
                this.prompt = s;
            }
            playingSound = this.prompt.Play(Input.Head.position + 0.5f * Input.Head.Forward);
            promptState = "promptName";
        }

        public void reset()
        {
            foreach (var target in this.targetList)
            {
                target.isDetected = false;
            }
        }
        public void save()
        {
            foreach (var target in this.targetList)
            {
                target.saveAsync();
            }
        }
        public void targetDetected(Target target)
        {
            target.isDetected = true;
            if (playing)
            {
                this.currentTarget = nextTarget();
                if (null == currentTarget)
                {
                    this.winSound.Play(target.pose.position);
                    playing = false;
                }
                else
                {
                    playingSound = this.foundSound.Play(target.pose.position);
                    promptState = "next";
                }
            }
        }
        public void targetSelected(Target target)
        {
            // only one target detected at a time
            foreach (var t in this.targetList)
            {
                t.isSelected = false;
            }
            target.isSelected = true;
        }
        public Boolean isFinished()
        {
            return (nextTarget() == null);
        }
        public Target nextTarget()
        {
            Target next = null;
            foreach (var target in this.targetList)
            {
                if (false == target.isDetected)
                {
                    next = target;
                    break;
                }
            }
            return next;
        }
        public int remaingTargetCount()
        {
            int count = 0;
            foreach (var target in this.targetList)
            {
                if (false == target.isDetected)
                {
                    count++;
                }
            }
            return count;

        }
        public Boolean isMemoRecorded()
        {
            // check if one target has a missing memo
            Boolean done = true;
            foreach (var target in this.targetList)
            {
                if (target.memo == null)
                {
                    done = false;
                    break;
                }
            }
            return done;
        }
        public void adminMode()
        {
            this.reset();
            currentTarget = null;
            mode = Game.ADMIN_MODE;
        }
        public void userMode()
        {
            this.reset();
            this.playing = false;
            this.currentTarget = null;
            mode = Game.USER_MODE;
        }
        public void addTarget(Pose pose, float diameter)
        {
            Model target = Model.FromMesh(
                //Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
                Mesh.GenerateSphere(1.0f),
                targetMaterial);
            string name = "target-" + targetList.Count;
            Target t = new Target(pose, target, diameter, name);
            targetList.Add(t);
            currentTarget = t;
        }
        public void deleteCurrentTarget()
        {         
            targetList.Remove(currentTarget);
            currentTarget = null;
        }
        private void stopRecording(Target target)
        {
            Microphone.Stop();
            target.memo = Sound.CreateStream((float)micIndex / 24000f);
            target.memo.WriteSamples(micBuffer, micIndex);

            //target.memo = Sound.Generate((t) =>
            //{
            //    int index = (int)(t * 48000f);
            //    return (index < micIndex) ? micBuffer[index] : 0;
            //},
            //micIndex / 48000f
            //);

        }
        private void handleRecording(Target target)
        {
            // UI.Text("Recording " + Microphone.IsRecording);
            // UI.Text("Samples " + Microphone.Sound.TotalSamples);
            // UI.Text("Unread " + Microphone.Sound.UnreadSamples);
            //  UI.Text("index " + micIndex);
            int samples = 0;
            if (Microphone.Sound.UnreadSamples > 0)
            {
                samples = Microphone.Sound.ReadSamples(ref soundChunk);
            }
            if (Microphone.IsRecording)
            {
                if (UI.ButtonRound("StopRecord", micSprite))
                {
                    stopRecording(target);
                }
                UI.SameLine(); UI.Label("Stop recording");

        

                // Read data from the microphone stream into our buffer, and track 
                // how much was actually read. Since the mic data collection runs in
                // a separate thread, this will often be a little inconsistent. Some
                // frames will have nothing ready, and others may have a lot!
                if (samples > 0)
                {
                    
                    int i = 0;
                    while ((micIndex < MAX_SOUND_LENGTH) && (i < samples))
                    {
                        micBuffer[micIndex] = soundChunk[i];
                        i += 1;
                        micIndex += 1;
                    }
                    
                }
                if (micIndex >= MAX_SOUND_LENGTH)
                {
                    stopRecording(target);
                }
            }
            else
            {
                if (UI.ButtonRound("StartRecord", micSprite))
                {
                    micIndex = 0;
                    Microphone.Start();
                }
                UI.SameLine(); UI.Label("record a short name");
                if (target.memo != null)
                {
                    if (UI.ButtonRound("Playback", speakerSprite))
                    {
                        target.memo.Play(Input.Head.position + 0.5f * Input.Head.Forward); // 
                    }
                    UI.SameLine();UI.Label("Play");
                }
                /*
                 if (target.memo != null) {
                    UI.Text("Duration " + target.memo.Duration);
                    UI.Text("Samples " + target.memo.TotalSamples);
                    UI.Text("Unread " + target.memo.UnreadSamples);
                }
                */
                
                 
                    
                
            }
         


        }
        public Boolean Step()
        {
            Boolean running = true;
            // test each target

            

            if (Game.ADMIN_MODE == mode)
            {
                foreach (var target in this.targetList)
                {
                    if (target.isTargetDetected(0f, targetMaterial, seenMaterial, selectedMaterial))
                    {
                        this.currentTarget = target;
                        targetSelected(target);
                    }
                }
            }
            else // user mode we test one target at a time
            {
                if ((this.playing) && (currentTarget != null))
                {
                    Material m = invisibleMaterial;
                    if (useIndicator)
                    {
                        m = hintMaterial;
                    }
                    if (currentTarget.isTargetDetected(1.5f, m, m, m, Game.TARGET_MIN_DISTANCE, true))
                    {
                        targetSelected(currentTarget);
                        targetDetected(currentTarget);

                    }
                }
            }

            // Target config UI panel
            //
            if ((Game.ADMIN_MODE == mode) && (currentTarget != null) && !currentTarget.isHandled)
            {
                // display the target panel config

                Vec3 toHead = (Input.Head.position - currentTarget.pose.position).Normalized;
                Pose infoPose = new Pose(currentTarget.pose.position + (toHead * 0.15f) - (Vec3.Up * 0.10f), Quat.LookDir(toHead));

                UI.WindowBegin("Target configuration", ref infoPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                UI.Label("size");
                UI.SameLine();
                UI.HSlider("size", ref currentTarget.scale, 0.05f, .3f, 0, 10 * U.cm);
                handleRecording(currentTarget);
                UI.NextLine();
                UI.NextLine(); 
                if (UI.ButtonRound("trash", trashSprite))
                {
                    deleteCurrentTarget();
                }
                    
                UI.WindowEnd();

            }

            // User voice prompts
            // wait for prompt to finish to play the memo of the target
            switch (promptState)
            {
                case "promptName":
                    if (playing && (this.currentTarget != null) && !playingSound.IsPlaying)
                    {
                        this.currentTarget.memo.Play(Input.Head.position + 0.5f * Input.Head.Forward);
                        promptState = "idle";
                    }
                    break;

                case "next":
                    if (playing && (this.currentTarget != null) && !playingSound.IsPlaying)
                    {
                        promptInstruction(promptNext);
                    }
                    break;
            }

            // Help sound
            if ((Game.USER_MODE == mode) && useSound) {
                if ((!playingSound.IsPlaying) &&  (this.currentTarget != null)) {
                    playingSound = targetSound.Play(this.currentTarget.pose.position, 0.6f);
                }
            }
            if ((Game.USER_MODE == mode) && useCompass && (this.currentTarget != null))
            {
                Vec3 compassPosition = Input.Head.position + Input.Head.Forward * 60 * U.cm - Vec3.Up * 10 * U.cm;
                Pose compassPose = new Pose(
                    compassPosition,
                    Quat.LookDir( compassPosition - this.currentTarget.pose.position )
                    );
                // TO DO : compass in flat hand and pointing to the target

                compass.Draw(compassPose.ToMatrix());
            }




            // User panel 
            // no draggable in user mode


            if (Game.ADMIN_MODE == mode)
            {
                UI.WindowBegin("Hunting board", ref this.windowUserPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                UI.Text("You are in design mode. This is a preview of the player board. Move it at a convenient place for the player.");
            }
            else
            {
                Pose forgetPose = new Pose(this.windowUserPose.position, this.windowUserPose.orientation);
                UI.WindowBegin("Hunting board", ref forgetPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                // pose is not save so handle has no effect 
            }

            if (isFinished())
            {
                UI.Text("Well done, you find all the hidden gems in this room !");
                UI.Label("Your time : ");
                UI.SameLine();
                int minutes = (int)huntingDuration / 60;
                int seconds = (int)huntingDuration % 60;
                String timer = minutes.ToString("00") + ":" + seconds.ToString("00");
                UI.Label(timer);

            }
            else
            {
                UI.Text("There are some interresting things in this room. Look around. Could you spot them ?");
                int ntarget = remaingTargetCount();
                UI.NextLine();
                UI.Text("You have to find " + ntarget + " target" + ((ntarget > 1) ? "s." : "."));

                if (playing)
                {
                        huntingDuration += Time.Elapsedf;
                }
            }
            if (!playing)
            {
                if (UI.Button("Start the hunt"))
                {
                    startHunt();
                }
            }
            else
            {
                if (UI.Button("Repeat instruction"))
                {
                    promptInstruction();
                }
                // display time spent 
               
                int minutes = (int)huntingDuration / 60;
                int seconds = (int)huntingDuration % 60;

                UI.HSeparator();
                String timer = minutes.ToString("00")+":"+seconds.ToString("00");
                UI.Text(timer, TextAlign.BottomCenter);
               
            }

            UI.HSeparator();
            UI.Text("Need some help? You can activate some clues...");
            
            String helpLabelSound = useSound ? "sound on" : "sound off";
            Sprite soundSprite = useSound ? onSprite : offSprite;
            if (UI.ButtonRound("StopRecord", soundSprite)) { useSound = !useSound; }
            UI.SameLine();UI.Label("Sound");


            Sprite visualSprite = useIndicator ? onSprite : offSprite;
            if (UI.ButtonRound("StopVisual", visualSprite)) { useIndicator = !useIndicator; }
            UI.SameLine(); UI.Label("Visual clue");

            Sprite compassSprite = useCompass ? onSprite : offSprite;
            if (UI.ButtonRound("StopCompass", compassSprite)) { useCompass = !useCompass; }
            UI.SameLine(); UI.Label("Compass");

            UI.HSeparator();


            if (UI.ButtonRound("Abort mission", powerSprite))
            {
                adminMode();
            }
            UI.SameLine(); UI.Label("Abort mission");

            UI.WindowEnd();
            // 
            // ADMIN UI
            //
            if (Game.ADMIN_MODE == mode)
            {
                UI.WindowBegin("Game Admin", ref this.windowAdminPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
                UI.Text("You are in design mode. Find interresting objects in the surrounding and place 'AR targets' on them. ");
                int ntarget = targetList.Count;
                UI.Text("You have added "+ntarget+" target"+((ntarget > 1) ? "s." : "." ));
                if (isMemoRecorded())
                {
                    if (UI.Button("Add target"))
                    {
                        Pose targetPose = new Pose(this.windowAdminPose.position + Vec3.Cross(Vec3.Up, this.windowAdminPose.Forward) * 30 * U.cm, Quat.Identity);

                        // TO DO : create a target just next to the Admin panel 
                        addTarget(targetPose, targetDiameter);
                    }
                    if ((targetList.Count > 0) && UI.Button("Switch to 'Play' mode"))
                    {
                        userMode();
                    }
                }
                else
                {
                    UI.Text("Please record a name for each target before adding a new one.");
                }



                if(UI.Button("Save targets"))
                {
                    save();
                }
                UI.NextLine();
                if (UI.ButtonRound("Exit", powerSprite)) running = false;
                UI.SameLine(); UI.Label("Exit Game");
                UI.WindowEnd();
            }



            return running;
        }
    }
}