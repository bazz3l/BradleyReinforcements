using System.Collections.Generic;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Bradley Reinforcements", "Bazz3l", "1.4.2")]
    [Description("Call for armed reinforcements when bradley is destroyed at launch site.")]
    public class BradleyReinforcements : RustPlugin
    {
        #region Fields
        
        private const string GUARD_PREFAB = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
        private const string CH47_PREFAB = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
        private const string PROTECTION_NAME = "BradleyGuardsProtection";
        private const string LANDING_NAME = "BradleyGuardsLandingZone";

        private readonly List<ScientistNPC> _guards = new List<ScientistNPC>();
        private static ProtectionProperties _ch47ProtectionProperties;
        private CH47HelicopterAIController _chinook;
        private CH47LandingZone _landingZone;
        private Quaternion _landingRotation;
        private Vector3 _monumentPosition;
        private Vector3 _landingPosition;
        private Vector3 _chinookPosition;
        private Vector3 _bradleyPosition;
        private bool _cleaningUp;
        
        private PluginConfig _config;

        #endregion

        #region Config

        protected override void LoadDefaultConfig() => _config = PluginConfig.DefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                    throw new JsonException();
            }
            catch
            {
                PrintWarning("Default config loaded.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private class PluginConfig
        {
            [JsonProperty(PropertyName = "ChatIcon (chat icon SteamID64)")]
            public ulong ChatIcon;

            [JsonProperty(PropertyName = "APCHealth (set starting health)")]
            public float APCHealth;

            [JsonProperty(PropertyName = "APCCrates (amount of crates to spawn)")]
            public int APCCrates;

            [JsonProperty(PropertyName = "NPCAmount (amount of guards to spawn max 11)")]
            public int NPCAmount;

            [JsonProperty(PropertyName = "InstantCrates (unlock crates when guards are eliminated)")]
            public bool InstantCrates;

            [JsonProperty(PropertyName = "DisableChinookDamage (should chinook be able to take damage)")]
            public bool DisableChinookDamage;

            [JsonProperty(PropertyName = "GuardSettings (create different types of guards must contain atleast 1)")]
            public List<GuardSetting> GuardSettings;
            
            [JsonProperty(PropertyName = "Chinook (spawn position)")]
            public Vector3 ChinookSpawnPosition = new Vector3(-193f, 150.0f, 13.2f);
            
            [JsonProperty(PropertyName = "Landing (spawn position)")]
            public Vector3 LandingSpawnPosition = new Vector3(162.3f, 3.0f, 7.4f);
            
            [JsonProperty(PropertyName = "Landing (spawn rotation)")]
            public Vector3 LandingSpawnRotation = new Vector3(0.0f, -109.8f, 0.0f);

            [JsonProperty("EffectiveWeaponRange (range weapons will be effective)")]
            public Dictionary<string, float> EffectiveWeaponRange = new Dictionary<string, float>
            {
                { "snowballgun", 60f },
                { "rifle.ak", 150f },
                { "rifle.bolt", 150f },
                { "bow.hunting", 30f },
                { "bow.compound", 30f },
                { "crossbow", 30f },
                { "shotgun.double", 10f },
                { "pistol.eoka", 10f },
                { "multiplegrenadelauncher", 50f },
                { "rifle.l96", 150f },
                { "rifle.lr300", 150f },
                { "lmg.m249", 150f },
                { "rifle.m39", 150f },
                { "pistol.m92", 15f },
                { "smg.mp5", 80f },
                { "pistol.nailgun", 10f },
                { "shotgun.waterpipe", 10f },
                { "pistol.python", 60f },
                { "pistol.revolver", 50f },
                { "rocket.launcher", 60f },
                { "shotgun.pump", 10f },
                { "pistol.semiauto", 30f },
                { "rifle.semiauto", 100f },
                { "smg.2", 80f },
                { "shotgun.spas12", 30f },
                { "speargun", 10f },
                { "smg.thompson", 30f }
            };

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    ChatIcon = 0,
                    APCHealth = 1000f,
                    APCCrates = 4,
                    NPCAmount = 8,
                    InstantCrates = true,
                    ChinookSpawnPosition = new Vector3(-193f, 150.0f, 13.2f),
                    LandingSpawnPosition = new Vector3(162.3f, 3.0f, 7.4f),
                    LandingSpawnRotation = new Vector3(0.0f, -109.8f, 0.0f),
                    GuardSettings = new List<GuardSetting> {
                        new GuardSetting
                        {
                            Name = "Heavy Gunner",
                            Health = 300f,
                            MaxRoamRadius = 80f,
                            MaxAggressionRange = 200f,
                        },
                        new GuardSetting
                        {
                            Name = "Light Gunner",
                            Health = 200f,
                            MaxRoamRadius = 80f,
                            MaxAggressionRange = 150f,
                        }
                    }
                };
            }

            public string ToJson() =>
                JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() =>
                JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }
        
        private class GuardSetting
        {
            [JsonProperty(PropertyName = "Name (custom display name)")]
            public string Name;

            [JsonProperty(PropertyName = "Health (set starting health)")]
            public float Health = 100f;

            [JsonProperty(PropertyName = "DamageScale (higher the value more damage)")]
            public float DamageScale = 0.2f;

            [JsonProperty(PropertyName = "MaxRoamRadius (max radius guards will roam)")]
            public float MaxRoamRadius = 30f;

            [JsonProperty(PropertyName = "MaxAggressionRange (distance guards will become aggressive)")]
            public float MaxAggressionRange = 200f;

            [JsonProperty(PropertyName = "KitName (custom kit name)")]
            public string KitName = "";

            [JsonProperty(PropertyName = "KitEnabled (enable custom kit)")]
            public bool KitEnabled = false;
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> 
            {
                {"EventStart", "<color=#30FEDE>Bradley Reinforcements</color>: Tank commander has sent for reinforcements, fight for your life."},
                {"EventEnded", "<color=#30FEDE>Bradley Reinforcements</color>: Reinforcements have been eliminated, try to loot up fast before other arrive."},
            }, this);
        }
        
        private string Lang(string key, string id = null, params object[] args) 
            => string.Format(lang.GetMessage(key, this, id), args);

        private void BroadcastMessage(string key) 
            => Server.Broadcast(Lang(key), _config.ChatIcon);

        #endregion

        #region Oxide

        private void OnServerInitialized()
        {
            HooksUnsubscribe();

            if (!FindLandingPoint()) 
                return;
            
            HooksSubscribe();
            CreateProtection();
        }

        private void Unload()
        {
            PerformCleanUp();
            DestroyProtection();
        }

        private void OnEntitySpawned(BradleyAPC bradley) 
            => OnAPCSpawned(bradley);

        private void OnEntityDeath(BradleyAPC bradley, HitInfo info) 
            => OnAPCDeath(bradley);

        private void OnEntityDeath(ScientistNPC npc, HitInfo info) 
            => OnNPCDeath(npc);

        private void OnEntityKill(ScientistNPC npc) 
            => OnNPCDeath(npc);
        
        private void OnEntityDismounted(BaseMountable mountable, ScientistNPC npc)
        {
            if (!_guards.Contains(npc) || !npc.HasBrain) 
                return;
            
            npc.Brain.Navigator.PlaceOnNavMesh();
            npc.Brain.Navigator.SetDestination(RandomCircle(_bradleyPosition, 5f));
        }

        private void OnFireBallDamage(FireBall fireball, ScientistNPC npc, HitInfo info)
        {
            if (_guards.Contains(npc) && info?.Initiator is FireBall)
            {
                info.DoHitEffects = false;
                info.damageTypes.ScaleAll(0f);                
            }
        }

        #endregion

        #region Subscribe Hook

        private void HooksSubscribe()
        {
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnEntityDeath));
            Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnEntityDismounted));
        }
        
        private void HooksUnsubscribe()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntityDismounted));
        }

        #endregion

        #region Event

        private void SpawnEvent()
        {
            _cleaningUp = false;
            
            _chinook = GameManager.server.CreateEntity(CH47_PREFAB, _chinookPosition, Quaternion.identity) as CH47HelicopterAIController;
            _chinook.Spawn();
            _chinook.SetLandingTarget(_landingPosition);
            _chinook.SetMinHoverHeight(1.5f);
            _chinook.CancelInvoke(_chinook.SpawnScientists);
            _chinook.gameObject.AddComponent<CH47LandedController>();

            if (_config.DisableChinookDamage)
                ApplyProtection(_chinook);

            SpawnGuards();
            
            BroadcastMessage("EventStart");
        }

        #region Oxide Hooks

        private void OnAPCSpawned(BradleyAPC bradley)
        {
            var position = bradley.transform.position;

            if (!IsInBounds(position)) 
                return;

            bradley.maxCratesToSpawn = _config.APCCrates;
            bradley._maxHealth = bradley._health = _config.APCHealth;
            bradley.health = bradley._maxHealth;

            ClearGuards();
        }

        private void OnAPCDeath(BradleyAPC bradley)
        {
            if (bradley == null || bradley.IsDestroyed) 
                return;

            var position = bradley.transform.position;

            if (!IsInBounds(position)) 
                return;

            _bradleyPosition = position;

            SpawnEvent();
        }

        private void OnNPCDeath(ScientistNPC npc)
        {
            if (_cleaningUp || !_guards.Remove(npc) || _guards.Count > 0) 
                return;

            if (_config.InstantCrates)
            {
                RemoveFlames();
                UnlockCrates();
            }

            BroadcastMessage("EventEnded");
        }
        
        #endregion

        #region Crates

        private void RemoveFlames()
        {
            var entities = Pool.GetList<FireBall>();

            Vis.Entities(_bradleyPosition, 25f, entities);

            foreach (var fireball in entities)
            {
                if (fireball.IsValid() && !fireball.IsDestroyed)
                    fireball.Extinguish();
            }

            Pool.FreeList(ref entities);
        }

        private void UnlockCrates()
        {
            var entities = Pool.GetList<LockedByEntCrate>();

            Vis.Entities(_bradleyPosition, 25f, entities);

            foreach (var crate in entities)
            {
                if (!crate.IsValid() || crate.IsDestroyed) 
                    continue;
                
                crate.SetLocked(false);
                
                if (crate.lockingEnt == null) 
                    continue;
                
                var entity = crate.lockingEnt.GetComponent<BaseEntity>();

                if (entity.IsValid() && !entity.IsDestroyed)
                    entity.Kill();
            }

            Pool.FreeList(ref entities);
        }    

        #endregion

        #region Guards
        
        private void SpawnGuards()
        {
            for (var i = 0; i < _config.NPCAmount - 1; i++)
                SpawnGuardAndMount(_config.GuardSettings.GetRandom(), _chinook.transform.position + _chinook.transform.forward * 10f, _bradleyPosition);

            for (var j = 0; j < 1; j++)
                SpawnGuardAndMount(_config.GuardSettings.GetRandom(), _chinook.transform.position - _chinook.transform.forward * 15f, _bradleyPosition);
        }
        
        private void SpawnGuardAndMount(GuardSetting settings, Vector3 position, Vector3 eventPos)
        {
            var npc = GameManager.server.CreateEntity(GUARD_PREFAB, position, Quaternion.identity) as ScientistNPC;
            npc.Spawn();
            npc.displayName = settings.Name;
            npc.startHealth = settings.Health;
            npc.damageScale = settings.DamageScale;
            npc.InitializeHealth(settings.Health, settings.Health);
            
            _chinook.AttemptMount(npc);
            
            _guards.Add(npc);

            GiveGuardLoadout(npc, settings);

            NextFrame(() =>
            {
                if (npc == null || npc.IsDestroyed) 
                    return;

                var roamPoint = RandomCircle(eventPos, 4f);

                npc.Brain.Navigator.Destination = roamPoint;
                npc.Brain.Navigator.Agent.agentTypeID = -1372625422;
                npc.Brain.Navigator.DefaultArea = "Walkable";
                npc.Brain.AllowedToSleep = false;
                npc.Brain.Navigator.Init(npc, npc.Brain.Navigator.Agent);
                npc.Brain.ForceSetAge(0);
                npc.Brain.states.Remove(AIState.TakeCover);
                npc.Brain.states.Remove(AIState.Flee);
                npc.Brain.states.Remove(AIState.Roam);
                npc.Brain.states.Remove(AIState.Chase);
                npc.Brain.Navigator.BestCoverPointMaxDistance = settings.MaxRoamRadius / 2;
                npc.Brain.Navigator.BestRoamPointMaxDistance = settings.MaxRoamRadius;
                npc.Brain.Navigator.MaxRoamDistanceFromHome = settings.MaxRoamRadius;
                
                npc.Brain.AddState(new TakeCoverState { brain = npc.Brain, Position = npc.Brain.Navigator.Destination });
                npc.Brain.AddState(new RoamState { brain = npc.Brain, Position = npc.Brain.Navigator.Destination });
                npc.Brain.AddState(new ChaseState { brain = npc.Brain });
                
                npc.Brain.Senses.Init(npc, 5f, settings.MaxAggressionRange, settings.MaxAggressionRange + 5f, -1f, true, true, true, settings.MaxAggressionRange, false, false, true, EntityType.Player, false);
            });
        }

        private void GiveGuardLoadout(ScientistNPC npc, GuardSetting settings)
        {
            if (settings.KitEnabled)
            {
                npc.inventory.Strip();
                
                Interface.Oxide.CallHook("GiveKit", npc, settings.KitName);
            }

            for (var i = 0; i < npc.inventory.containerBelt.itemList.Count; i++)
            {
                var item = npc.inventory.containerBelt.itemList[i];
                if (item == null) 
                    continue;

                var projectile = item.GetHeldEntity() as BaseProjectile;
                if (projectile == null) 
                    continue;

                projectile.effectiveRange = _config.EffectiveWeaponRange.ContainsKey(item.info.shortname) ? 
                    _config.EffectiveWeaponRange[item.info.shortname] : 
                    settings.MaxAggressionRange;

                projectile.CanUseAtMediumRange = true;
                projectile.CanUseAtLongRange = true;
            }

            npc.EquipWeapon();
        }
        
        private class RoamState : ScientistBrain.BasicAIState
        {
            private float _nextRoamPositionTime;
            public Vector3 Position;

            public RoamState() : base(AIState.Roam) {  }

            public override void StateEnter()
            {
                Reset();

                base.StateEnter();

                _nextRoamPositionTime = 0.0f;
            }

            public override float GetWeight() => 0.0f;

            private Vector3 GetDestination() => Position;

            private void SetDestination(Vector3 destination) => brain.Navigator.SetDestination(destination, BaseNavigator.NavigationSpeed.Fast);

            public override StateStatus StateThink(float delta)
            {
                if (Vector3.Distance(GetDestination(), GetEntity().transform.position) > 10.0 && _nextRoamPositionTime < Time.time)
                {
                    var insideUnitSphere = UnityEngine.Random.insideUnitSphere;
                    insideUnitSphere.y = 0.0f;
                    insideUnitSphere.Normalize();

                    SetDestination(GetDestination() + insideUnitSphere * 2f);

                    _nextRoamPositionTime = Time.time + UnityEngine.Random.Range(0.5f, 1f);
                }

                return StateStatus.Running;
            }
        }

        private class ChaseState : ScientistBrain.BasicAIState
        {
            private StateStatus _status = StateStatus.Error;
            private float _nextPositionUpdateTime;

            public ChaseState() : base(AIState.Chase)
            {
                AgrresiveState = true;
            }

            public override void StateEnter()
            {
                Reset();

                base.StateEnter();

                _status = StateStatus.Error;

                if (brain.PathFinder == null)
                    return;

                _status = StateStatus.Running;

                _nextPositionUpdateTime = 0.0f;
            }

            public override void StateLeave()
            {
                base.StateLeave();

                Stop();
            }

            public override StateStatus StateThink(float delta)
            {
                if (_status == StateStatus.Error)
                    return _status;

                var baseEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (baseEntity == null)
                    return StateStatus.Error;

                var entity = (ScientistNPC)GetEntity();

                float num2 = Vector3.Distance(baseEntity.transform.position, entity.transform.position);

                if (brain.Senses.Memory.IsLOS(baseEntity) || (double)num2 <= 30.0)
                    brain.Navigator.SetFacingDirectionEntity(baseEntity);
                else
                    brain.Navigator.ClearFacingDirectionOverride();

                brain.Navigator.SetCurrentSpeed(num2 <= 30.0
                    ? BaseNavigator.NavigationSpeed.Normal
                    : BaseNavigator.NavigationSpeed.Fast);

                if (_nextPositionUpdateTime < Time.time)
                {
                    _nextPositionUpdateTime = Time.time + UnityEngine.Random.Range(0.5f, 1f);

                    brain.Navigator.SetDestination(baseEntity.transform.position, BaseNavigator.NavigationSpeed.Normal);
                }

                return brain.Navigator.Moving
                    ? StateStatus.Running
                    : StateStatus.Finished;
            }

            private void Stop()
            {
                brain.Navigator.Stop();
                brain.Navigator.ClearFacingDirectionOverride();
            }
        }

        private class TakeCoverState : ScientistBrain.BasicAIState
        {
            private StateStatus _status = StateStatus.Error;
            private BaseEntity coverFromEntity;
            public Vector3 Position;

            public TakeCoverState() : base(AIState.TakeCover) {  }

            public override void StateEnter()
            {
                Reset();

                base.StateEnter();

                _status = StateStatus.Running;

                if (StartMovingToCover())
                    return;

                _status = StateStatus.Error;
            }

            public override void StateLeave()
            {
                base.StateLeave();

                brain.Navigator.ClearFacingDirectionOverride();

                ClearCoverPointUsage();
            }

            private void ClearCoverPointUsage()
            {
                var aiPoint = brain.Events.Memory.AIPoint.Get(4);
                if (aiPoint != null)
                    aiPoint.ClearIfUsedBy(GetEntity());
            }

            private bool StartMovingToCover() => brain.Navigator.SetDestination(Position, BaseNavigator.NavigationSpeed.Normal);

            public override StateStatus StateThink(float delta)
            {
                FaceCoverFromEntity();

                if (_status == StateStatus.Error)
                    return _status;

                return brain.Navigator.Moving ? StateStatus.Running : StateStatus.Finished;
            }

            private void FaceCoverFromEntity()
            {
                coverFromEntity = brain.Events.Memory.Entity.Get(brain.Events.CurrentInputMemorySlot);
                if (coverFromEntity == null)
                    return;

                brain.Navigator.SetFacingDirectionEntity(coverFromEntity);
            }
        }

        #endregion

        #region Cleanup

        private void PerformCleanUp()
        {
            ClearGuards();
            ClearZones();
        }

        private void ClearZones()
        {
            if (_landingZone != null) 
                UnityEngine.Object.Destroy(_landingZone.gameObject);

            _landingZone = null;
        }

        private void ClearGuards()
        {
            _cleaningUp = true;
            
            for (var i = 0; i < _guards.Count; i++)
            {
                var npc = _guards[i];
                if (npc.IsValid() && !npc.IsDestroyed)
                    npc.Invoke(() => npc?.KillMessage(), 0.25f);
            }
            
            _guards.Clear();
        }

        #endregion

        #region Landing

        private void CreateLandingZone()
        {
            _landingZone = new GameObject(LANDING_NAME)
            {
                transform =
                {
                    position = _landingPosition,
                    rotation = _landingRotation
                }
            }.AddComponent<CH47LandingZone>();
        }

        private bool FindLandingPoint()
        {
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (!monument.gameObject.name.Contains("launch_site_1")) 
                    continue;

                CreateLandingPoint(monument);
                CreateLandingZone();

                return true;
            }

            return false;
        }

        private void CreateLandingPoint(MonumentInfo monument)
        {
            _monumentPosition = monument.transform.position;
            _chinookPosition = monument.transform.TransformPoint(_config.ChinookSpawnPosition);
            _landingPosition = monument.transform.TransformPoint(_config.LandingSpawnPosition);
            _landingRotation = Quaternion.Euler(monument.transform.rotation.eulerAngles + _config.LandingSpawnRotation);
        }
        
        #endregion

        #region Controller

        private class CH47LandedController : MonoBehaviour
        {
            private CH47HelicopterAIController _chinook;

            private void Awake()
            {
                _chinook = GetComponent<CH47HelicopterAIController>();

                InvokeRepeating(nameof(CheckDropped), 5f, 5f);
            }

            private void OnDestroy()
            {
                CancelInvoke();

                if (_chinook.IsValid() && !_chinook.IsDestroyed)
                    _chinook.Invoke(_chinook.DelayedKill, 10f);
            }

            private void CheckDropped()
            {
                if (_chinook.NumMounted() > 0) 
                    return;

                Destroy(this);
            }
        }

        #endregion

        #endregion
        
        #region Protection
        
        private void CreateProtection()
        {
            _ch47ProtectionProperties = ScriptableObject.CreateInstance<ProtectionProperties>();
            _ch47ProtectionProperties.name = PROTECTION_NAME;
            _ch47ProtectionProperties.Add(1);
        }

        private void DestroyProtection()
        {
            ScriptableObject.Destroy(_ch47ProtectionProperties);
            
            _ch47ProtectionProperties = null;
        }

        private void ApplyProtection(BaseEntity entity)
        {
            var combatEntity = entity as BaseCombatEntity;
            if (combatEntity != null)
                combatEntity.baseProtection = _ch47ProtectionProperties;
        }

        #endregion

        #region Helpers
        
        private bool IsInBounds(Vector3 position) => Vector3.Distance(_monumentPosition, position) <= 300f;

        private Vector3 RandomCircle(Vector3 center, float radius)
        {
            var ang = UnityEngine.Random.value * 360;
            
            Vector3 pos = center;
            pos.x = center.x + radius * Mathf.Sin(ang * Mathf.Deg2Rad);
            pos.z = center.z + radius * Mathf.Cos(ang * Mathf.Deg2Rad);
            pos.y = center.y;
            
            return pos;
        }
        
        #endregion
    }
}