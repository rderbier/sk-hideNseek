using System;
using System.Collections.Generic;
using StereoKit;
using Windows.Storage;
using Windows.Storage.Streams;
using Newtonsoft.Json.Linq;
using Windows.Perception.Spatial;
using System.Threading.Tasks;

namespace hideNseek
{

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
        private float[] soundChunk;
        private Int32 micIndex;
        private Sprite powerSprite, micSprite, trashSprite, speakerSprite, onSprite, offSprite;
        private Model compass;
        private Boolean playing = false;
        private Double huntingDuration = 0f;
        private SpatialAnchorStore anchorStore;
        private SpatialLocator locator;
        private Boolean isRecording = false;


        public Game()
        {

            initMaterials();

            initSharedResources();
            initHolograms();
            initUI();
 
            adminMode();

            World.OcclusionEnabled = true;
            InitStoreAsync();

            initMicrophoneRecording();

        }
        private void initMaterials()
        {
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
            invisibleMaterial[MatParamName.ColorTint] = new Color(0, 0, 0, 0.0f);
            invisibleMaterial.Wireframe = false;

            hintMaterial = Default.Material.Copy(); //matAlphaBlend
            hintMaterial.Transparency = Transparency.Blend;
            hintMaterial.DepthWrite = false;
            hintMaterial[MatParamName.ColorTint] = new Color(1f, 1f, 1f, 0.1f);
        }
        private void initSharedResources()
        {
            // init Sounds, Sprites ...
            foundSound = Sound.FromFile("sound_success.wav");
            winSound = Sound.FromFile("welldone.wav");
            promptFind = Sound.FromFile("tofind.wav");
            promptNext = Sound.FromFile("nextfind.wav");
            targetSound = Sound.FromFile("radar-sound.wav");

            

            powerSprite = Sprite.FromFile("power.png", SpriteType.Single);
            micSprite = Sprite.FromFile("microphone.png", SpriteType.Single);
            speakerSprite = Sprite.FromFile("speaker.png", SpriteType.Single);
            trashSprite = Sprite.FromFile("trash.png", SpriteType.Single);
            onSprite = Sprite.FromFile("on.png", SpriteType.Single);
            offSprite = Sprite.FromFile("off.png", SpriteType.Single);
        }
        private void initHolograms()
        {
            Material compassMaterial = Material.Default.Copy();
            compass = Model.FromMesh(MeshUtils.createArrow(0.01f, 0.005f, 0.06f), compassMaterial);
        }
        private void initUI()
        {
            // 
            // set UI scheme
            Color uiColor = Color.HSV(.83f, 0.33f, 1f, 0.8f);
            UI.ColorScheme = uiColor;
            windowAdminPose = new Pose(-.2f, 0, -0.65f, Quat.LookAt(new Vec3(-.2f, 0, -0.5f), Input.Head.position, Vec3.Up));
            windowUserPose = new Pose(+.4f, 0, -0.65f, Quat.LookAt(new Vec3(+.4f, 0, -0.5f), Input.Head.position, Vec3.Up));
        }
        private void initMicrophoneRecording()
        {
            // Create assets used by the game
            micBuffer = new float[MAX_SOUND_LENGTH]; // 5 seconds
            soundChunk = new float[MAX_SOUND_LENGTH]; // 0.5 second max
            micIndex = 0;
            // Mic Issue : Start Recording but ignore the first memo
            isRecording = false; // not saving the memo
            Microphone.Start(); // but start the Mic to drain the buffer once !
        }

        private void tryRestoreAnchors()
        {

            IReadOnlyDictionary<string, SpatialAnchor> anchors = anchorStore.GetAllSavedAnchors();

            foreach (KeyValuePair<string, SpatialAnchor> anchor in anchors)
            {

                if (World.FromPerceptionAnchor(anchor.Value, out Pose at))
                {
                    addTarget(at, targetDiameter, anchor.Key, anchor.Value);
                }
            }

        }
        private async Task InitStoreAsync()
        {
            locator = SpatialLocator.GetDefault();
            anchorStore = await SpatialAnchorManager.RequestStoreAsync();
            if (anchorStore != null)
            {
                tryRestoreAnchors();
            }
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
        public async void  save()
        {
            foreach (var target in this.targetList)
            {
                 target.saveAsync();
            }
            // create a world locked anchor where the origin is the current position of the Hololens
            SpatialStationaryFrameOfReference referenceFrame = locator.CreateStationaryFrameOfReferenceAtCurrentLocation();
            SpatialAnchor originAnchor = SpatialAnchor.TryCreateRelativeTo(referenceFrame.CoordinateSystem);
            
            foreach (var target in this.targetList)
            {
                target.anchorTarget(anchorStore, locator, referenceFrame, originAnchor);        
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
        public void addTarget(Pose pose, float diameter, String name = null,  SpatialAnchor anchor = null)
        {
            Model target = Model.FromMesh(
                //Mesh.GenerateRoundedCube(Vec3.One * 0.1f, 0.02f),
                Mesh.GenerateSphere(1.0f),
                targetMaterial);
            Target t = new Target(pose, target, diameter, name, ref anchor);
            targetList.Add(t);
            currentTarget = t;
        }
        public  void deleteCurrentTarget()
        {
            if (currentTarget != null)
            {
                currentTarget.removeAnchor(anchorStore);
                currentTarget.clean();
                targetList.Remove(currentTarget);
            }
            currentTarget = null;
        }
        private void stopRecording(Target target)
        {
            Microphone.Stop();
            isRecording = false;
            if (target != null)
            {
                target.memo = Sound.CreateStream((float)micIndex / 24000f);
                target.memo.WriteSamples(micBuffer, micIndex);
            }

        }
        
        private void handleRecording()
        {
            int samples = 0;
            
            if (Microphone.IsRecording)
            {   
                int unread = Microphone.Sound.UnreadSamples;
                if (unread > 0)
                {
                    // trying to workaround an issue when using the microphone
                    // seems that sometimes the ReadSamples does not change the Sound buffer index !
                    // so repeat the read untill it seems to work !
                    do
                    {
                        samples = Microphone.Sound.ReadSamples(ref soundChunk);
                    } while (Microphone.Sound.UnreadSamples > 48);

                }
              
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
                    if (isRecording == true)
                    {
                        stopRecording(currentTarget);
                    } else
                    {
                        Microphone.Stop();
                        // Mic issue workaround : start game by recording a bit and ignore the first 5s 
                    }
                }
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
                    // detect after gaze for 1.2 s 
                    if (currentTarget.isTargetDetected(1.2f, m, m, m, false, Game.TARGET_MIN_DISTANCE, true))
                    {
                        targetSelected(currentTarget);
                        targetDetected(currentTarget);

                    }
                }
            }

            // Recording

            handleRecording();
            //
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
                
                if (Microphone.IsRecording)
                {
                    if (UI.ButtonRound("StopRecord", micSprite))
                    {
                        stopRecording(currentTarget);
                    }
                    UI.SameLine(); UI.Label("Stop recording");

                }
                else
                {
                    if (UI.ButtonRound("StartRecord", micSprite))
                    {
                        micIndex = 0;
                        isRecording = true;
                        Microphone.Start();
                    }
                    UI.SameLine(); UI.Label("record a short name");
                    if (currentTarget.memo != null)
                    {
                        if (UI.ButtonRound("Playback", speakerSprite))
                        {
                            currentTarget.memo.Play(Input.Head.position + 0.5f * Input.Head.Forward); // 
                        }
                        UI.SameLine(); UI.Label("Play");
                    }
                  
                }
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

            displayUserPanel();
            // 
            // ADMIN UI
            //
            if (Game.ADMIN_MODE == mode)
            {
                running = displayAdminPanel();
            }

            return running;
        }
        private Boolean displayAdminPanel()
        {
            Boolean running = true;
            UI.WindowBegin("Game Admin", ref this.windowAdminPose, new Vec2(25, 0) * U.cm, UIWin.Normal);
            UI.Text("You are in design mode. Find interresting objects in the surrounding and place 'AR targets' on them. ");
            int ntarget = targetList.Count;
            UI.Text("You have added " + ntarget + " points of interest" + ((ntarget > 1) ? "s." : "."));
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
                UI.Text("Record a name for each point of interest before adding a new one.");
            }



            if (UI.Button("Save targets"))
            {
                save();
            }
            UI.NextLine();
            if (UI.ButtonRound("Exit", powerSprite)) running = false;
            UI.SameLine(); UI.Label("Exit Game");
            UI.WindowEnd();
            return running;
        }
        private void displayUserPanel()
        {
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
                UI.Text("Well done, you have found all the points of interest in this room !");
                UI.Label("Your time : ");
                UI.SameLine();
                int minutes = (int)huntingDuration / 60;
                int seconds = (int)huntingDuration % 60;
                String timer = minutes.ToString("00") + ":" + seconds.ToString("00");
                UI.Label(timer);

            }
            else
            {
                UI.Text("There are some interresting things in this room. Look around. Can you spot them ?");
                int ntarget = remaingTargetCount();
                UI.NextLine();
                UI.Text("You have to find " + ntarget + " object" + ((ntarget > 1) ? "s." : "."));

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
                String timer = minutes.ToString("00") + ":" + seconds.ToString("00");
                UI.Text(timer, TextAlign.BottomCenter);

            }

            UI.HSeparator();
            UI.Text("Need some help? You can activate some clues...");

            String helpLabelSound = useSound ? "sound on" : "sound off";
            Sprite soundSprite = useSound ? onSprite : offSprite;
            if (UI.ButtonRound("StopRecord", soundSprite)) { useSound = !useSound; }
            UI.SameLine(); UI.Label("Sound");


            Sprite visualSprite = useIndicator ? onSprite : offSprite;
            if (UI.ButtonRound("StopVisual", visualSprite)) { useIndicator = !useIndicator; }
            UI.SameLine(); UI.Label("Visual clue");

            Sprite compassSprite = useCompass ? onSprite : offSprite;
            if (UI.ButtonRound("StopCompass", compassSprite)) { useCompass = !useCompass; }
            UI.SameLine(); UI.Label("Compass");

            UI.HSeparator();


            if (UI.ButtonRound("Abort mission", powerSprite))
            {
                playing = false;
                huntingDuration = 0;
                adminMode();
            }
            UI.SameLine(); UI.Label("Abort mission");

            UI.WindowEnd();
        }
    }
}