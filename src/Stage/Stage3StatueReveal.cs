using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[RequireComponent(typeof(BoxCollider), typeof(DamageReceiver))]
public sealed class Stage3StatueReveal : MonoBehaviour
{
    [Header("균열과 폭발")]
    [FormerlySerializedAs("_destructionWindup")]
    [SerializeField, Min(0.1f), Tooltip("방어 목표 파괴 또는 1차 제한시간 만료부터 폭발까지의 공통 시간(초).")]
    private float _windup = 3.25f;
    [SerializeField, Range(0.01f, 0.99f), Tooltip("폭발 전 구간에서 전체 균열이 완성될 시점의 비율. 0.68이면 68% 지점까지 균열이 퍼집니다.")]
    private float _crackSpreadEnd = 0.68f;
    [SerializeField, Min(0.1f), Tooltip("폭발 후 파편이 튀고 바닥에 머물다가 사라지기까지의 시간(초).")]
    private float _debrisDuration = 1.75f;

    [Header("빛줄기 타이밍")]
    [SerializeField, Range(0f, 0.99f), Tooltip("폭발 전 구간에서 첫 빛줄기가 나오는 시점의 비율. 전체 균열이 완성될 때까지 기다립니다.")]
    private float _lightRayStart = 0.80f;
    [SerializeField, Range(0f, 1f), Tooltip("빛줄기 구간에서 첫 줄과 마지막 줄의 시작 시차가 차지하는 비율. 0이면 동시에 시작합니다.")]
    private float _lightRayStagger = 0.40f;
    [SerializeField, Range(0.01f, 1f), Tooltip("빛줄기 구간에서 한 줄이 끝까지 뻗는 데 걸리는 시간의 비율. 작을수록 빠릅니다.")]
    private float _lightRayGrowthTime = 0.80f;

    [Header("빛줄기 모양")]
    [SerializeField, Min(0.01f), Tooltip("빛줄기 기본 길이에 곱하는 배율. 각 줄의 방향과 상대 길이는 유지합니다.")]
    private float _lightRayLengthMultiplier = 1.55f;
    [SerializeField, Min(0.001f), Tooltip("가장 굵은 빛줄기 끝의 폭을 석상 높이에 대한 비율로 설정합니다. 0.128이면 약 13%입니다.")]
    private float _lightRayTipWidth = 0.128f;
    [SerializeField, Min(0f), Tooltip("빛줄기 밝기 배율. 1은 현재 기본 밝기, 0은 빛줄기를 보이지 않게 합니다.")]
    private float _lightRayBrightness = 1f;

    [Header("연출 리소스")]
    [SerializeField] private Shader _crackShader;
    [SerializeField] private Mesh _fragmentMesh;
    [SerializeField] private Material _fragmentMaterial;
    [SerializeField] private Material _dustMaterial;

    private const float FragmentSpin = 430f;
    private const float FragmentBounceSpin = 0.16f;

    private static readonly (float Angle, float Height, float Lift, float Length)[] LightRayLayout =
    {
        // 상단과 몸통의 균열 사이로 긴 빛줄기가 여러 방향으로 뻗습니다.
        (20f, 0.61f, 0.20f, 0.47f),
        (44f, 0.81f, 0.45f, 0.43f),
        (71f, 0.71f, 0.38f, 0.53f),
        (96f, 0.62f, 0.11f, 0.37f),
        (126f, 0.50f, 0.09f, 0.44f),
        (178f, 0.78f, 0.30f, 0.49f),
        (209f, 0.84f, 0.49f, 0.44f),
        (240f, 0.59f, 0.17f, 0.52f),
        (275f, 0.74f, 0.26f, 0.39f),
        (307f, 0.69f, 0.32f, 0.45f),
        (339f, 0.54f, 0.14f, 0.41f),

        // 받침 위에서 몸통이 가늘어지는 꼬리 부분에는 짧은 빛줄기를 둡니다.
        (17f, 0.35f, 0.12f, 0.23f),
        (137f, 0.42f, 0.18f, 0.30f),
        (232f, 0.37f, 0.07f, 0.25f),
        (314f, 0.44f, 0.22f, 0.28f)
    };

    private readonly List<(MeshRenderer Renderer, Mesh Mesh, MaterialPropertyBlock Properties)> _statueMeshes = new();
    private readonly List<(LineRenderer Line, Vector3 Start, Vector3 End, float Width, float Delay)> _rays = new();
    private readonly List<(Transform Transform, Vector3 Start, Vector3 End, Vector3 SpinAxis,
        Vector3 Scale, float GroundOffset, float ArcHeight, float FlightFraction, Quaternion Rotation)> _fragments = new();

    private BoxCollider _body;
    private DamageReceiver _receiver;
    private GameObject _effects;
    private Material _crackMaterial;
    private GameObject _lightRays;
    private Material _lightRayMaterial;
    private Bounds _visualBounds;
    private Light _flash;
    private Color _crackColor;
    private float _startedAt;
    private float _duration;
    private float _burstAt;
    private bool _playing;
    private bool _broken;

    internal bool IsReady => !_playing || Time.time >= _startedAt + _duration;

    private void Awake()
    {
        _body = GetComponent<BoxCollider>();
        _receiver = GetComponent<DamageReceiver>();
    }

    internal bool TryBegin()
    {
        if (!isActiveAndEnabled || _playing || _broken || _fragmentMesh == null ||
            _fragmentMaterial == null || _dustMaterial == null)
            return false;

        _playing = true;
        _startedAt = Time.time;
        _duration = _windup;
        _crackColor = new Color(1f, 0.055f, 0.015f);
        _receiver.SetInvulnerable(true);

        if (_crackShader != null && _crackShader.isSupported)
            _crackMaterial = new Material(_crackShader);

        CacheStatueVisuals();
        _effects = new GameObject("Minotos Statue Break VFX");
        _effects.transform.SetParent(transform, false);
        if (_crackMaterial != null)
            CreateLightRays();

        _flash = new GameObject("Seal Glow").AddComponent<Light>();
        _flash.transform.SetParent(_effects.transform, false);
        _flash.transform.localPosition = _visualBounds.center;
        _flash.type = LightType.Point;
        _flash.shadows = LightShadows.None;
        _flash.color = _crackColor;
        _flash.range = 5f;
        _flash.intensity = 0f;
        Debug.Log("Stage 3 석상 붕괴 예고: 균열과 빛줄기 재생", this);
        return true;
    }

    private void CreateLightRays()
    {
        _lightRayMaterial = new Material(_crackShader);
        _lightRayMaterial.SetFloat("_LightRayMode", 1f);
        _lightRays = new GameObject("Statue Crack Light Rays");
        _lightRays.transform.SetParent(_effects.transform, false);
        _lightRays.SetActive(false);

        float statueHeight = _visualBounds.size.y;
        AnimationCurve rayWidthCurve = AnimationCurve.Linear(0f, 0.16f, 1f, 1f);

        // 빛줄기의 시작점을 석상 내부에 두고 원본 메시의 깊이로 가려 균열 밖으로 새어 나오게 합니다.
        for (int index = 0; index < LightRayLayout.Length; index++)
        {
            var ray = LightRayLayout[index];
            float angle = ray.Angle * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 start = _visualBounds.center + outward * statueHeight * 0.06f;
            start.y = _visualBounds.min.y + statueHeight * ray.Height;
            Vector3 end = start + (outward + Vector3.up * ray.Lift).normalized *
                (statueHeight * ray.Length * _lightRayLengthMultiplier);

            var line = new GameObject("Crack Light Ray").AddComponent<LineRenderer>();
            line.transform.SetParent(_lightRays.transform, false);
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            // 중간점 없이 일정한 기울기로 폭이 넓어지는 직선을 만듭니다.
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, start);
            float widthScale = Mathf.Clamp(ray.Length / 0.44f, 0.6f, 1f);
            line.widthCurve = rayWidthCurve;
            float width = statueHeight * _lightRayTipWidth * widthScale;
            line.widthMultiplier = width;
            line.sharedMaterial = _lightRayMaterial;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            float delay = _lightRayStagger * index / (LightRayLayout.Length - 1);
            _rays.Add((line, start, end, width, delay));
        }
    }

    private void CacheStatueVisuals()
    {
        _visualBounds = new Bounds(_body.center, _body.size);

        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                continue;

            Mesh mesh = filter.sharedMesh;
            Matrix4x4 toStatue = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Bounds meshBounds = mesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                    (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                    (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
                _visualBounds.Encapsulate(toStatue.MultiplyPoint3x4(point));
            }

            var properties = new MaterialPropertyBlock();
            properties.SetMatrix("_MeshToStatue", toStatue);
            _statueMeshes.Add((renderer, mesh, properties));
        }
    }

    private void Update()
    {
        if (_playing)
        {
            float elapsed = Time.time - _startedAt;
            float progress = Mathf.Clamp01(elapsed / _duration);
            float pulse = 0.83f + 0.17f * Mathf.Sin(progress * progress * 36f);
            _flash.intensity = progress * progress * 2f * pulse;
            DrawCracks(progress, pulse);
            UpdateLightRays(progress, pulse);
        }
        else if (_broken && _effects != null)
        {
            UpdateFragments();
        }
    }

    private void UpdateLightRays(float progress, float pulse)
    {
        float lightRayStart = Mathf.Max(_lightRayStart, _crackSpreadEnd);
        if (_lightRays == null || progress <= lightRayStart)
            return;

        if (!_lightRays.activeSelf)
            _lightRays.SetActive(true);

        float rayProgress = Mathf.InverseLerp(lightRayStart, 1f, progress);
        foreach (var ray in _rays)
        {
            float growth = Mathf.Clamp01((rayProgress - ray.Delay) / _lightRayGrowthTime);
            if (growth <= 0f)
                continue;

            float remaining = 1f - growth;
            float length = 1f - remaining * remaining;
            ray.Line.SetPosition(1, Vector3.Lerp(ray.Start, ray.End, length));
            ray.Line.widthMultiplier = ray.Width * Mathf.Lerp(0.48f, 1f, length);
            if (!ray.Line.enabled)
                ray.Line.enabled = true;
        }

        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rayProgress / 0.14f));
        float brightness = fadeIn * Mathf.Lerp(0.95f, 1.6f, rayProgress) * Mathf.Lerp(0.82f, 1f, pulse);
        Color tint = Color.Lerp(_crackColor, new Color(1f, 0.88f, 0.70f), 0.84f);
        _lightRayMaterial.SetColor("_CrackColor", tint * (brightness * _lightRayBrightness));
    }

    private void DrawCracks(float progress, float pulse)
    {
        if (_crackMaterial == null)
            return;

        _crackMaterial.SetFloat("_CrackProgress", Mathf.Clamp01(progress / _crackSpreadEnd));
        _crackMaterial.SetColor("_CrackColor", _crackColor * Mathf.Lerp(0.9f, 1.85f, progress) * pulse);
        _crackMaterial.SetVector("_StatueBounds", new Vector4(
            _visualBounds.center.x, _visualBounds.min.y, _visualBounds.center.z, _visualBounds.size.y));

        // 원래 메시 위에 발광 균열만 그려 각도에 관계없이 표면에 붙어 보이게 합니다.
        foreach (var part in _statueMeshes)
        {
            var renderParams = new RenderParams(_crackMaterial)
            {
                layer = part.Renderer.gameObject.layer,
                worldBounds = part.Renderer.bounds,
                matProps = part.Properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            for (int index = 0; index < part.Mesh.subMeshCount; index++)
                Graphics.RenderMesh(renderParams, part.Mesh, index, part.Renderer.transform.localToWorldMatrix);
        }
    }

    internal void Break()
    {
        if (!_playing)
            return;

        _playing = false;
        _broken = true;
        _burstAt = Time.time;
        if (_lightRays != null)
            _lightRays.SetActive(false);
        foreach (var part in _statueMeshes)
            part.Renderer.enabled = false;
        _body.enabled = false;

        _flash.color = Color.Lerp(_crackColor, Color.white, 0.6f);
        _flash.intensity = 3.2f;
        ScatterFragments();
        EmitDust();
        Destroy(_effects, _debrisDuration);
        Debug.Log("Stage 3 석상 붕괴: 파편과 먼지 재생", this);
    }

    private void ScatterFragments()
    {
        Vector3 origin = transform.TransformPoint(_visualBounds.center);
        float height = _visualBounds.size.y * Mathf.Abs(transform.lossyScale.y);
        float floor = origin.y - height * 0.5f;
        int groundLayers = LayerMask.GetMask("Floor", "BuildArea");
        Bounds meshBounds = _fragmentMesh.bounds;
        float meshSize = Mathf.Max(meshBounds.size.magnitude, 0.01f);
        const int fragmentCount = 58;
        var random = new System.Random();
        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        float startAngle = Range(0f, Mathf.PI * 2f);

        // 각 조각의 착지 지점을 미리 찾아 충돌로 플레이에 영향을 주지 않고 바닥에 남깁니다.
        for (int index = 0; index < fragmentCount; index++)
        {
            float angle = startAngle + Mathf.PI * 2f * (index + Range(0f, 1f)) / fragmentCount;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float size = Range(0.07f, index % 6 == 0 ? 0.36f : 0.23f);
            Vector3 start = origin + direction * Range(0.05f, 0.45f);
            start.y += height * Range(-0.36f, 0.4f);
            Vector3 end = origin + direction * Range(0.8f, 3.6f);
            end.y = floor;
            if (Physics.Raycast(end + Vector3.up * 2f, Vector3.down, out RaycastHit hit,
                4f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                end.y = hit.point.y;
            }
            var fragment = new GameObject("Bronze Fragment");
            float scale = size / meshSize;
            var spinAxis = new Vector3(direction.z, Range(-0.65f, 0.65f), -direction.x).normalized;
            var rotation = Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f));
            Quaternion settledRotation = Quaternion.AngleAxis(FragmentSpin * (1f + FragmentBounceSpin), spinAxis) * rotation;
            Vector3 localUp = Quaternion.Inverse(settledRotation) * Vector3.up;
            Vector3 absoluteUp = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
            float groundOffset = scale * (Vector3.Dot(absoluteUp, meshBounds.extents) -
                Vector3.Dot(localUp, meshBounds.center));
            end.y += groundOffset;

            fragment.transform.SetPositionAndRotation(start, rotation);
            fragment.transform.localScale = Vector3.one * scale;
            fragment.transform.SetParent(_effects.transform, true);
            fragment.AddComponent<MeshFilter>().sharedMesh = _fragmentMesh;
            var renderer = fragment.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _fragmentMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            _fragments.Add((fragment.transform, start, end, spinAxis,
                fragment.transform.localScale, groundOffset, Range(0.25f, 1.35f), Range(0.43f, 0.62f), rotation));
        }
    }

    private void UpdateFragments()
    {
        float progress = Mathf.Clamp01((Time.time - _burstAt) / _debrisDuration);
        _flash.intensity = 3.2f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.18f)));

        // 비행, 짧은 바운스, 정지 순으로 진행한 뒤 마지막 구간에 조각을 줄입니다.
        foreach (var fragment in _fragments)
        {
            float flight = Mathf.Clamp01(progress / fragment.FlightFraction);
            float bounce = Mathf.Clamp01((progress - fragment.FlightFraction) / 0.12f);
            Vector3 position = Vector3.Lerp(fragment.Start, fragment.End, flight);
            position.y += 4f * flight * (1f - flight) * fragment.ArcHeight;
            position.y += Mathf.Sin(bounce * Mathf.PI) * 0.13f;

            float vanish = Mathf.InverseLerp(0.83f, 1f, progress);
            position.y -= fragment.GroundOffset * vanish;
            float spin = (flight + bounce * FragmentBounceSpin) * FragmentSpin;
            fragment.Transform.SetPositionAndRotation(position,
                Quaternion.AngleAxis(spin, fragment.SpinAxis) * fragment.Rotation);
            fragment.Transform.localScale = fragment.Scale * (1f - vanish);
        }
    }

    private void EmitDust()
    {
        var dust = new GameObject("Stone Dust").AddComponent<ParticleSystem>();
        dust.transform.SetParent(_effects.transform, false);
        dust.transform.localPosition = _visualBounds.center - Vector3.up * _visualBounds.extents.y * 0.7f;
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = dust.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.1f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_debrisDuration * 0.42f, _debrisDuration * 0.78f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startColor = new Color(0.82f, 0.75f, 0.63f, 0.32f);

        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.55f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var emission = dust.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)34) });

        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = _dustMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var fade = dust.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.16f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        dust.Play();
    }

    internal void Cancel()
    {
        if (_playing)
            ClearEffects();
    }

    private void OnDisable() => ClearEffects();

    private void ClearEffects()
    {
        if (_playing || _broken)
            _receiver.SetInvulnerable(false);
        if (_broken)
        {
            _body.enabled = true;
            foreach (var part in _statueMeshes)
            {
                if (part.Renderer != null)
                    part.Renderer.enabled = true;
            }
        }

        _statueMeshes.Clear();
        _rays.Clear();
        _fragments.Clear();
        if (_effects != null)
        {
            _effects.SetActive(false);
            Destroy(_effects);
        }
        if (_crackMaterial != null)
            Destroy(_crackMaterial);
        if (_lightRayMaterial != null)
            Destroy(_lightRayMaterial);

        _lightRays = null;
        _lightRayMaterial = null;
        _playing = false;
        _broken = false;
    }
}
