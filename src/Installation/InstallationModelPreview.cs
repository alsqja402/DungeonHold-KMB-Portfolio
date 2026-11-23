using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal sealed class InstallationModelPreview
{
    private static readonly int ColorProperty = Shader.PropertyToID("_OutlineColor");
    private static readonly Color AvailableColor = new Color(0.10f, 0.67f, 1f);
    private static readonly Color BlockedColor = new Color(1f, 0.15f, 0.10f);
    private const uint AvailableRenderingLayer = 1u << 6;
    private const uint BlockedRenderingLayer = 1u << 7;

    private readonly GameObject _root;
    private readonly Material _outlineMaterial;
    private readonly List<MeshRenderer> _lineworkRenderers;
    private readonly List<Mesh> _bakedMeshes;
    private bool? _wasAvailable;

    private InstallationModelPreview(
        GameObject root, Material outlineMaterial, List<MeshRenderer> lineworkRenderers,
        List<Mesh> bakedMeshes)
    {
        _root = root;
        _outlineMaterial = outlineMaterial;
        _lineworkRenderers = lineworkRenderers;
        _bakedMeshes = bakedMeshes;
    }

    public static InstallationModelPreview Create(
        PlacedInstallation prefab, Scene scene, int layer, bool useLinework)
    {
        Transform source = prefab.PreviewVisualRoot;
        Shader shader = prefab.PreviewOutlineShader;
        if (source == null || source.parent != prefab.transform ||
            (!useLinework && (shader == null || !shader.isSupported)))
        {
            return null;
        }

        GameObject root = new GameObject(prefab.name + " Preview");
        root.SetActive(false);
        root.transform.localScale = prefab.transform.localScale;
        SceneManager.MoveGameObjectToScene(root, scene);
        Material outlineMaterial = useLinework ? null : new Material(shader);
        List<MeshRenderer> lineworkRenderers = useLinework ? new List<MeshRenderer>() : null;
        var bakedMeshes = new List<Mesh>();
        var preview = new InstallationModelPreview(root, outlineMaterial, lineworkRenderers, bakedMeshes);

        if (!preview.CopyVisuals(source, root.transform, layer, isPreviewRoot: true))
        {
            preview.Release();
            return null;
        }

        return preview;
    }

    public void Show(Vector3 position, Quaternion rotation, bool isAvailable)
    {
        _root.transform.SetPositionAndRotation(position, rotation);

        if (_wasAvailable != isAvailable)
        {
            UpdateOutline(isAvailable);
            _wasAvailable = isAvailable;
        }

        if (!_root.activeSelf)
        {
            _root.SetActive(true);
        }
    }

    public void Hide()
    {
        if (_root != null && _root.activeSelf)
        {
            _root.SetActive(false);
        }
    }

    public void Release()
    {
        Hide();
        Object.Destroy(_root);
        Object.Destroy(_outlineMaterial);

        foreach (Mesh mesh in _bakedMeshes)
        {
            Object.Destroy(mesh);
        }
        _bakedMeshes.Clear();
    }

    private void UpdateOutline(bool isAvailable)
    {
        if (_lineworkRenderers == null)
        {
            _outlineMaterial.SetColor(
                ColorProperty, isAvailable ? AvailableColor : BlockedColor);
            return;
        }

        uint stateLayer = isAvailable ? AvailableRenderingLayer : BlockedRenderingLayer;
        uint outlineLayers = AvailableRenderingLayer | BlockedRenderingLayer;
        foreach (MeshRenderer renderer in _lineworkRenderers)
        {
            renderer.renderingLayerMask =
                (renderer.renderingLayerMask & ~outlineLayers) | stateLayer;
        }
    }

    private bool CopyVisuals(Transform source, Transform parent, int layer, bool isPreviewRoot = false)
    {
        if (!source.gameObject.activeSelf)
        {
            return false;
        }

        GameObject copy = new GameObject(source.name);
        copy.layer = layer;
        Transform target = copy.transform;
        target.SetParent(parent, false);
        target.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        target.localScale = source.localScale;

        bool hasMesh = false;
        if (source.TryGetComponent(out MeshRenderer renderer) &&
            (renderer.enabled || isPreviewRoot) &&
            source.TryGetComponent(out MeshFilter filter) &&
            filter.sharedMesh != null &&
            filter.sharedMesh.subMeshCount > 0)
        {
            AddPreviewMesh(copy, filter.sharedMesh, renderer);
            hasMesh = true;
        }
        else if (source.TryGetComponent(out SkinnedMeshRenderer skinned) &&
                 skinned.enabled && skinned.sharedMesh != null &&
                 skinned.sharedMesh.subMeshCount > 0)
        {
            // 장전 화살 등의 스킨 메시를 정적인 프리뷰 메시로 복사
            Mesh mesh = new Mesh { name = source.name + " Preview Mesh" };
            skinned.BakeMesh(mesh);
            _bakedMeshes.Add(mesh);
            AddPreviewMesh(copy, mesh, skinned);
            hasMesh = true;
        }

        foreach (Transform child in source)
        {
            hasMesh |= CopyVisuals(child, target, layer);
        }

        return hasMesh;
    }

    private void AddPreviewMesh(GameObject target, Mesh mesh, Renderer source)
    {
        MeshRenderer model = AddRenderer(target, mesh, source.sharedMaterials);
        model.renderingLayerMask = source.renderingLayerMask;

        if (_lineworkRenderers != null)
        {
            _lineworkRenderers.Add(model);
            return;
        }

        GameObject outline = new GameObject("Outline");
        outline.layer = target.layer;
        outline.transform.SetParent(target.transform, false);
        Material[] materials = new Material[mesh.subMeshCount];
        System.Array.Fill(materials, _outlineMaterial);
        AddRenderer(outline, mesh, materials);
    }

    private static MeshRenderer AddRenderer(GameObject target, Mesh mesh, Material[] materials)
    {
        target.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = target.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }
}
