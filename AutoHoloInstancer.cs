using UnityEngine;

public class AutoHoloInstancer : MonoBehaviour
{
    public bool mbInstancedBase;
    public int mnInstancedID = -1;
    public BlueprintBuilderMod.HoloInstancerTypes mType = BlueprintBuilderMod.HoloInstancerTypes.InstancerCount;
    public Vector3 mUnityOffset = new Vector3(0.5f, 0.5f, 0.5f);
    //GameObjectWrapper mWrap;
    public Vector3 position;
    public Quaternion rotation;
    public bool AllowRotation = true;
    byte mFlags;

    void AssignInstancer()
    {
        //This will always be null because it's not being spawned by the SpawnableObjectManager...
        //Requires a reference to the object itself?
        //mWrap = GetComponent<SpawnableObjectScript>().wrapper;

        //if (mWrap == null)
        //{
        //    Debug.Log("AutoHoloInstancer trying to assign instancer but wrapper is null");
        //    return;
        //}

        if (position == null)
        {
            Debug.Log("AutoHoloInstance trying to assign instancer but position is null");
            return;
        }
        mnInstancedID = BlueprintBuilderMod.HoloInstancers[(int)mType].TryAdd();
        UpdateInstancer();
    }

    void UpdateInstancer()
    {
        Vector3 lUnityPos = position + mUnityOffset;

        //Quaternion lQuat = SegmentCustomRenderer.GetRotationQuaternion(mWrap.mFlags);
        BlueprintBuilderMod.HoloInstancers[(int)mType].SetMatrixQuat(mnInstancedID, lUnityPos, rotation, Vector3.one);
        //mFlags = mWrap.mFlags;
    }

    void Update()
    {
        if (PersistentSettings.mbHeadlessServer)
        {
            this.enabled = false;
            return;
        }
        if (mnInstancedID == -1)
        {
            AssignInstancer();
        }
    }

    void OnDestroy()
    {
        if (mnInstancedID != -1)
            BlueprintBuilderMod.HoloInstancers[(int)mType].Remove(mnInstancedID);
        mnInstancedID = -1;
    }
}