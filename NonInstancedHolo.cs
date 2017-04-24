using UnityEngine;

public class NonInstancedHolo : MonoBehaviour
{
    public BlueprintBuilderMod.HoloInstancerTypes mType = BlueprintBuilderMod.HoloInstancerTypes.InstancerCount;
    public Vector3 position;
    public Quaternion rotation;

    public void Update()
    {
        Graphics.DrawMesh(HologramCubes.meshes[(int)mType], position , rotation, HologramCubes.materials[(int)mType], 0);
    }

}

