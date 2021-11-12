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
        private String anchorID;
        private SpatialAnchor anchor;

        public Target(Pose pose, Model target, float scale, String name, ref SpatialAnchor anchor)
        {
            this.pose = new Pose(pose.position, pose.orientation);
            this.model = target;
            this.scale = scale;
            
            this.name = (name != null) ? name : "target-" + Guid.NewGuid().ToString();
            this.anchor = anchor;
            if (anchor != null)
            {
                this.anchorID = this.name;
                anchor.RawCoordinateSystemAdjusted += this.OnCoordinateSystemAdjusted;
            }
            this.memo = null;
            this.gazeTime = 0f;
            tryRestore();
        }
        private void OnCoordinateSystemAdjusted(SpatialAnchor sender, SpatialAnchorRawCoordinateSystemAdjustedEventArgs args)
        {
            if (World.FromPerceptionAnchor(this.anchor, out Pose at))
            {
                this.pose = at;
            }

        }

        public Boolean isTargetDetected(float gazeDuration, Material hide, Material seen, Material selected, Boolean isDraggable=true, float distance = 2.0f, Boolean gazeIndicator = false)
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
            if (isDraggable)
            {
                this.isHandled = UI.Handle(this.name, ref this.pose, scaledBounds);
            }
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
                    // saving metadata
                    Windows.Storage.StorageFile confiFile =
                       await storageFolder.CreateFileAsync(
                           "config-" + this.name + ".json",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    //String json = String.Format("{ \"scale\":\"{0:F4}\" }", this.scale);
                    JObject o = new JObject();
                    o["scale"] = this.scale;
                    await Windows.Storage.FileIO.WriteTextAsync(confiFile, o.ToString());
                } catch (Exception ex)
                {
                    // ignore file errors for now !
                }
            }

        }
        public async void clean()
        {
            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile dataFile = await storageFolder.GetFileAsync("memo-" + this.name);
            if (dataFile != null)
            {
                await dataFile.DeleteAsync();
            }
            Windows.Storage.StorageFile confiFile = await storageFolder.GetFileAsync("config-" + this.name + ".json");
            if (confiFile != null)
            {
                await confiFile.DeleteAsync();
            }

        }
        public void removeAnchor(SpatialAnchorStore anchorStore)
        {
            if (this.anchorID != null)
            {
                anchorStore.Remove(this.anchorID);
                this.anchorID = null;
            }
        }
        public String anchorTarget(SpatialAnchorStore anchorStore, SpatialLocator locator, SpatialStationaryFrameOfReference referenceFrame = null , SpatialAnchor originAnchor = null)
        {
            if (anchorStore != null)
            {
                // create a world locked anchor where the origin is the current position of the Hololens
                if (referenceFrame == null)
                {
                    referenceFrame = locator.CreateStationaryFrameOfReferenceAtCurrentLocation();
                }
                if (originAnchor == null)
                {
                    originAnchor = SpatialAnchor.TryCreateRelativeTo(referenceFrame.CoordinateSystem);
                }

                Pose originPose = World.FromPerceptionAnchor(originAnchor);
                Pose poseInReferenceFrame = originPose.ToMatrix().Inverse.Transform(new Pose(this.pose.position, this.pose.orientation));
                SpatialAnchor targetAnchor = SpatialAnchor.TryCreateRelativeTo(referenceFrame.CoordinateSystem, poseInReferenceFrame.position, poseInReferenceFrame.orientation);

                removeAnchor(anchorStore);

                if (targetAnchor != null)
                {
                    anchorID = this.name;
                    this.anchorID = anchorStore.TrySave(anchorID, targetAnchor) ? anchorID : null;
                }
                return this.anchorID;
            } else
            {
                return null;
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
                Windows.Storage.StorageFile configFile = await storageFolder.GetFileAsync("config-" + this.name+".json");
                if (configFile != null)
                {
                    string text = await Windows.Storage.FileIO.ReadTextAsync(configFile);
                    var details = JObject.Parse(text);
                    if (details["scale"]!= null)
                    {
                        this.scale = (float)details["scale"];
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
        private float[] soundChunk;
        private Int32 micIndex;
        private Sprite powerSprite, micSprite, trashSprite, speakerSprite, onSprite, offSprite;
        private Model compass;
        private Boolean playing = false;
        private Double huntingDuration = 0f;
        private SpatialAnchorStore anchorStore;
        private SpatialLocator locator;

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

        public Game()
        {
            
            // Create assets used by the game
            micBuffer = new float[MAX_SOUND_LENGTH]; // 5 seconds
            soundChunk = new float[MAX_SOUND_LENGTH]; // 0.5 second max
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
            invisibleMaterial[MatParamName.ColorTint] = new Color(0, 0, 0, 0.0f);
            invisibleMaterial.Wireframe = false;

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
            

            // set UI scheme
            Color uiColor = Color.HSV(.83f, 0.33f, 1f, 0.8f);
            UI.ColorScheme = uiColor;

            World.OcclusionEnabled = true;
            InitStoreAsync();
            

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
                    stopRecording(currentTarget);
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