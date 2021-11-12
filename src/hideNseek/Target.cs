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

        public Boolean isTargetDetected(float gazeDuration, Material hide, Material seen, Material selected, Boolean isDraggable = true, float distance = 2.0f, Boolean gazeIndicator = false)
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
                }
                catch (Exception ex)
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
        public String anchorTarget(SpatialAnchorStore anchorStore, SpatialLocator locator, SpatialStationaryFrameOfReference referenceFrame = null, SpatialAnchor originAnchor = null)
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
            }
            else
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
                Windows.Storage.StorageFile configFile = await storageFolder.GetFileAsync("config-" + this.name + ".json");
                if (configFile != null)
                {
                    string text = await Windows.Storage.FileIO.ReadTextAsync(configFile);
                    var details = JObject.Parse(text);
                    if (details["scale"] != null)
                    {
                        this.scale = (float)details["scale"];
                    }

                }
            }
            catch (Exception e)
            {
                // ignore errors
            }
        }
    }
    
}