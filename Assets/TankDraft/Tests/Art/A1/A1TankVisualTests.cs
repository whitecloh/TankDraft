using System;
using NUnit.Framework;
using TankDraft.Art.A1;
using UnityEngine;

namespace TankDraft.Art.A1.Tests
{
    public sealed class A1TankVisualTests
    {
        private GameObject _host;
        private A1TankVisualConfig _config;
        private Material _sharedMaterial;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("A1VisualTestHost");
            _config = ScriptableObject.CreateInstance<A1TankVisualConfig>();
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _sharedMaterial = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_sharedMaterial);
            UnityEngine.Object.DestroyImmediate(_config);
            UnityEngine.Object.DestroyImmediate(_host);
        }

        [Test]
        public void Validate_RejectsMissingConfigTierCountAndRenderers()
        {
            var visual = _host.AddComponent<A1TankVisual>();
            visual.Configure(null, new A1TankVisual.TierBinding[2]);

            Assert.That(visual.Validate(out var message), Is.False);
            Assert.That(message, Does.Contain("Visual config is required."));
            Assert.That(message, Does.Contain("Exactly three tier bindings are required."));

            visual.Configure(_config, CreateBindings(rendererCount: 2));
            Assert.That(visual.Validate(out message), Is.False);
            Assert.That(message, Does.Contain("exactly three renderers"));
        }

        [Test]
        public void TeamStatesAndTierSelection_KeepExactlyOneTierActive()
        {
            var visual = CreateVisual(out var bindings);
            for (var tier = 1; tier <= 3; tier++)
            {
                for (var enemyState = 0; enemyState < 2; enemyState++)
                {
                    visual.SetVisualTier(tier);
                    visual.SetEnemyTeam(enemyState == 1);

                    Assert.That(visual.VisualTier, Is.EqualTo(tier));
                    Assert.That(visual.IsEnemyTeam, Is.EqualTo(enemyState == 1));
                    for (var index = 0; index < bindings.Length; index++)
                    {
                        Assert.That(bindings[index].root.activeSelf, Is.EqualTo(index == tier - 1));
                    }
                }
            }
        }

        [Test]
        public void CustomColorPersistsAcrossTierSwitchesWithoutMaterialInstances()
        {
            var visual = CreateVisual(out var bindings);
            var expected = new Color(0.21f, 0.43f, 0.87f, 1f);
            var originalMaterial = bindings[0].renderers[0].sharedMaterial;

            visual.SetTeamColor(expected);
            visual.SetVisualTier(3);

            for (var tier = 0; tier < bindings.Length; tier++)
            {
                for (var renderer = 0; renderer < bindings[tier].renderers.Length; renderer++)
                {
                    var block = new MaterialPropertyBlock();
                    bindings[tier].renderers[renderer].GetPropertyBlock(block);
                    Assert.That(block.GetColor("_TeamColor"), Is.EqualTo(expected));
                    Assert.That(bindings[tier].renderers[renderer].sharedMaterial, Is.SameAs(originalMaterial));
                }
            }
        }

        [Test]
        public void YawAndRecoilContinueAcrossTierSwitchAndResetForPoolRestoresAllBindPoses()
        {
            var visual = CreateVisual(out var bindings);
            visual.SetTurretYaw(45f);
            visual.SetRecoil(0.4f);
            var tierOneRotation = bindings[0].turret.localRotation;
            var tierOnePosition = bindings[0].barrelRecoil.localPosition;

            visual.SetVisualTier(2);

            Assert.That(Quaternion.Angle(bindings[1].turret.localRotation, tierOneRotation), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(bindings[1].barrelRecoil.localPosition, tierOnePosition), Is.LessThan(0.001f));

            visual.ResetForPool();
            for (var index = 0; index < bindings.Length; index++)
            {
                Assert.That(Quaternion.Angle(bindings[index].turret.localRotation, Quaternion.identity), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(bindings[index].barrelRecoil.localPosition, new Vector3(0f, 0f, 0.25f)), Is.LessThan(0.001f));
            }
        }

        [Test]
        public void HitFlashDoesNotReplaceTeamColorAndClearsOnReuse()
        {
            var visual = CreateVisual(out var bindings);
            visual.SetEnemyTeam(true);
            visual.SetFlash(1f);
            visual.SetVisualTier(3);
            var block = new MaterialPropertyBlock();
            bindings[2].renderers[0].GetPropertyBlock(block);
            Assert.That(block.GetColor("_TeamColor"), Is.EqualTo(_config.EnemyColor));
            Assert.That(block.GetFloat("_HitFlash"), Is.EqualTo(1f));
            visual.ResetForPool();
            bindings[2].renderers[0].GetPropertyBlock(block);
            Assert.That(block.GetFloat("_HitFlash"), Is.Zero);
            Assert.That(block.GetColor("_TeamColor"), Is.EqualTo(_config.FriendlyColor));
        }

        [Test]
        public void SetVisualTier_RejectsOutOfRangeTier()
        {
            var visual = CreateVisual(out _);
            Assert.Throws<ArgumentOutOfRangeException>(() => visual.SetVisualTier(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => visual.SetVisualTier(4));
        }

        private A1TankVisual CreateVisual(out A1TankVisual.TierBinding[] bindings)
        {
            var visual = _host.AddComponent<A1TankVisual>();
            bindings = CreateBindings(rendererCount: 3);
            visual.Configure(_config, bindings);
            Assert.That(visual.Validate(out var message), Is.True, message);
            return visual;
        }

        private A1TankVisual.TierBinding[] CreateBindings(int rendererCount)
        {
            var bindings = new A1TankVisual.TierBinding[3];
            for (var tier = 0; tier < bindings.Length; tier++)
            {
                var root = new GameObject($"Tier{tier + 1}");
                root.transform.SetParent(_host.transform, false);
                var turret = new GameObject("Turret").transform;
                turret.SetParent(root.transform, false);
                var barrel = new GameObject("Barrel").transform;
                barrel.SetParent(turret, false);
                barrel.localPosition = new Vector3(0f, 0f, 0.25f);
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(barrel, false);
                var hit = new GameObject("Hit").transform;
                hit.SetParent(root.transform, false);

                var renderers = new Renderer[rendererCount];
                for (var index = 0; index < rendererCount; index++)
                {
                    var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mesh.transform.SetParent(root.transform, false);
                    mesh.name = $"Mesh{index + 1}";
                    renderers[index] = mesh.GetComponent<Renderer>();
                    renderers[index].sharedMaterial = _sharedMaterial;
                }

                bindings[tier] = new A1TankVisual.TierBinding
                {
                    root = root,
                    turret = turret,
                    barrelRecoil = barrel,
                    muzzle = muzzle,
                    hit = hit,
                    renderers = renderers
                };
            }

            return bindings;
        }
    }
}
