using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Sirenix.OdinInspector;

namespace AllHailTemos
{
    #region ENUMS
    public enum ActorDetectProblemType
    {
        None,

        HasInputButNoMovement,
        FallingTooLong,
        AirborneTooLong,
    }

    public enum ActorDetectFixType
    {
        None,

        DisableAirborneInLocation,
        TeleportToSafeGround,
    }
    #endregion

    //PURPOSE: Historical records of fixes, so I can review them later and see what was going on, and how it was fixed
    [System.Serializable]
    public class ActorDetectProblemAndFixActorHistoryItem
    {
        #region FIELDS
        [Tooltip("What was the problem?")]
        public ActorDetectProblemType Problem;

        [Tooltip("What was the last problem?  Need record of any flipping")]
        public ActorDetectProblemType ProblemMaybeLast;

        [Tooltip("What did we do to fix it?")]
        public ActorDetectFixType Fix;

        [Tooltip("When did we first discover this may be a problem")]
        public float TimeDiscoveredInitial;

        [Tooltip("When did we discover it?")]
        public float TimeDiscovered;

        [Tooltip("When did we fix it?")]
        public float TimeFixed;

        [Tooltip("Initial position, when maybe problem")]
        public Vector3 PosInitial;

        [Tooltip("Before-fix position, so we can see the difference from initial")]
        public Vector3 PosBeforeFix;
        #endregion

        public ActorDetectProblemAndFixActorHistoryItem(ActorDetectProblemAndFixActor actor, ActorDetectFixType fix, float timeFixed, Vector3 posBeforeFix)
        {
            Problem = actor.ProblemCurrent;
            ProblemMaybeLast = actor.ProblemMaybeLast;

            Fix = fix;

            TimeDiscovered = actor.TimeDiscoveredProblem;   // Time the first Maybe was discovered, that lead to a problem
            TimeFixed = timeFixed;

            PosInitial = actor.PosInitial;
            PosBeforeFix = posBeforeFix;
        }
    }

    [System.Serializable]
    public class ActorDetectProblemAndFixActor
    {
        #region FIELDS
        [Tooltip("Actor we are tracking for fixes")]
        public ActorData Actor;

        public bool IsProblem { get { return ProblemCurrent != ActorDetectProblemType.None ? true : false; } }
        public bool IsProblemMaybe { get { return ProblemMaybe != ActorDetectProblemType.None ? true : false; } }

        [Tooltip("Is this a problem?  Not until the max duration is reached.")]
        public ActorDetectProblemType ProblemMaybe;
        [Tooltip("What was the last problem?  We check for flipping problems here")]
        public ActorDetectProblemType ProblemMaybeLast;

        [Tooltip("What is the problem?  After ProblemMaybe is set past max time, it becomes ProblemCurrent")]
        public ActorDetectProblemType ProblemCurrent;

        [Tooltip("When did we initially discover this may be a problem?")]
        public float TimeDiscoveredInitial;

        [Tooltip("When did we discover this problem?  The first time ProblemCurrent becomes not-None, until the fix is applied")]
        public float TimeDiscoveredProblem;

        [Tooltip("Problem and Fix history.  Every time we make a fix, it gets recorded here.   All discovered problems will be fixed after duration.")]
        public List<ActorDetectProblemAndFixActorHistoryItem> History;

        [Tooltip("If they are airborne.  Not checked if they are flying or swimming.")]
        public bool IsAirborne;
        [Tooltip("When did we start to be airborne?  Will test duration")]
        public float TimeAirborneStart;

        [Tooltip("Is the actor falling?")]
        public bool IsFalling;
        [Tooltip("When did we start to descend?  Will test duration")]
        public float TimeFallingStart;

        [Tooltip("How far is this actor to standable ground?  If not found, then float.MaxValue")]
        public float DistanceToGround;

        [Tooltip("What was our position when we initially found this problem?")]
        public Vector3 PosInitial;

        [Tooltip("What was our position the last time we checked?  Just save it every time we search or process")]
        public Vector3 PosLastChecked;
        #endregion

        public ActorDetectProblemAndFixActor(ActorData actor)
        {
            ResetData();

            Actor = actor;

            History = new List<ActorDetectProblemAndFixActorHistoryItem>();
        }

        public void ResetData()
        {
            ProblemCurrent = ActorDetectProblemType.None;
            ProblemMaybe = ActorDetectProblemType.None;
            ProblemMaybeLast = ActorDetectProblemType.None;

            // Set reasonable min/max values as defaults, to reset
            TimeDiscoveredInitial = float.MinValue;
            TimeDiscoveredProblem = float.MinValue;

            IsFalling = false;
            TimeFallingStart = float.MinValue;

            IsAirborne = false;
            TimeAirborneStart = float.MinValue;

            DistanceToGround = float.MaxValue;

            // Set invalid value
            PosInitial = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            PosLastChecked = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        #region DETERMINE_PROBLEM
        private bool DetermineProblem_Falling(ActorDetectProblemAndFix parent)
        {
            // Are we falling?  Really.  Aren't we all?
            return Actor_IsFalling();
        }

        private bool DetermineProblem_Airborne(ActorDetectProblemAndFix parent)
        {
            // If they are NOT falling, but they are also NOT on the ground (on ground and movable)
            return !Actor_IsFalling() && !Actor_IsGrounded();
        }

        private bool DetermineProblem_HasInputButNoMovement(ActorDetectProblemAndFix parent)
        {
            var curPos = Actor_GetPosition();

            // If we are still in the same position, as when we had the maybe-problem, OR, position is the same as the last time we checked/searched/processed this actor
            if (curPos == PosInitial || curPos == PosLastChecked)
            {
                if (Actor_HasInputNotZero()) return true;
            }

            // Made it through the gauntlet.  Did not find a problem
            return false;
        }

        //PURPOSE: Normally called by ProcessUpdate(), can also be invoked to determine if this is a ProblemActor.  Return IsProblem.
        public bool DetermineProblem(ActorDetectProblemAndFix parent)
        {
            ActorDetectProblemType problem = ActorDetectProblemType.None;

            // Ensure we know our distance to the ground for any operations.  This will do a raycast down, storing the distance, and the Actor.GroundedStatusTarget
            Actor_GetDistanceToGround();

            // Look for problems.  Put them in order of escallation.  Falling before Airborne, because Falling is a sub-set of Airborne.  Simple ordering.
            if (DetermineProblem_Falling(parent)) problem = ActorDetectProblemType.FallingTooLong;
            else if (DetermineProblem_Airborne(parent)) problem = ActorDetectProblemType.AirborneTooLong;
            else if (DetermineProblem_HasInputButNoMovement(parent)) problem = ActorDetectProblemType.HasInputButNoMovement;

            // If we found a problem (not None), record it
            if (problem != ActorDetectProblemType.None)
            {
                DiscoveredProblemMaybe(problem);
            }

            // If we are maybe in a problem
            if (IsProblemMaybe)
            {
                // While we continuously have this maybe-problem, how long until we decide it's an actual problem?
                var maxTimeUntilProblem = parent.GetMaxTimeBeforeProblem(ProblemMaybe);
                if (maxTimeUntilProblem <= Time.time - TimeDiscoveredInitial)
                {
                    // We passed the max time threshold, and this is an actual problem now
                    DiscoveredProblem(ProblemMaybe);
                }
            }
            // Else we are not maybe in a problem, so clear our data, so that we stop accumulating any time on Maybe Problems of the past.  They have to stay problems...
            else
            {
                ResetData();
            }

            // Always get our last position here.  This is needed to START the tests for the position being the same for a long time (problem if also has Movement Input)
            PosLastChecked = Actor_GetPosition();

            return IsProblem;
        }

        private void DiscoveredProblemMaybe(ActorDetectProblemType problem, bool logProblem = true)
        {
            // Save the current Maybe to last, in case we are toggling problems.  Troubleshooting.  Need a list?  Probably not once its working, just 1 diagnosis
            ProblemMaybeLast = ProblemMaybe;
            ProblemMaybe = problem;

            // If this is a new problem set, mark the time
            if (TimeDiscoveredInitial == float.MinValue) TimeDiscoveredInitial = Time.time;

            // Get the position of this actor when we discovered the problem.  Maybe we are not moving.
            PosInitial = Actor_GetPosition();

            if (logProblem) Debug.Log($"Actor Detect Problem: {Actor.Name}  Maybe Problem: {ProblemMaybe}  Last Maybe: {ProblemMaybeLast}");
        }

        private void DiscoveredProblem(ActorDetectProblemType problem, bool logProblem = true)
        {
            // Set the current problem
            ProblemCurrent = problem;

            // If this is a new problem set, mark the time
            if (TimeDiscoveredProblem == float.MinValue) TimeDiscoveredProblem = Time.time;

            if (logProblem) Debug.Log($"Actor Detect Problem: {Actor.Name}  Problem: {ProblemCurrent}  Last: {ProblemMaybeLast}  History: {History.Count}");
        }
        #endregion

        #region FIX_PROBLEM
        //PURPOSE: If we have a problem, and the duration is met, then fix it
        public bool DetermineFix(ActorDetectProblemAndFix parent)
        {
            // Nothing to fix
            if (!IsProblem) return false;

            // How long until we have to fix our current problem?  Default is MaxValue, so never.
            var durationToFix = parent.GetDurationToFix(ProblemCurrent);

            // This is when we determined it was a Real Problem.  After the   
            var durationSinceDiscovered = Time.time - TimeDiscoveredProblem;

            Debug.Log($"Determine Fix: Apply Now?  {durationToFix} <= {Time.time} - {TimeDiscoveredProblem} == {durationSinceDiscovered}");

            // Have we passed the duration to fix it?
            if (durationSinceDiscovered > durationToFix)
            {
                switch (ProblemCurrent)
                {
                    // Requires Teleport to safe area
                    case ActorDetectProblemType.HasInputButNoMovement:
                    case ActorDetectProblemType.FallingTooLong:
                        FixCurrentProblem(parent, ActorDetectFixType.TeleportToSafeGround);
                        break;

                    // Can be snapped to ground in-place
                    case ActorDetectProblemType.AirborneTooLong:
                        FixCurrentProblem(parent, ActorDetectFixType.DisableAirborneInLocation);
                        break;
                }
            }

            return IsProblem;
        }

        private void FixCurrentProblem_DisableAirborneInLocation(ActorDetectProblemAndFix parent)
        {
            Actor_SnapToGround();
        }

        private void FixCurrentProblem_TeleportToSafeGround(ActorDetectProblemAndFix parent)
        {
            // We will teleport to an area that can get a navmesh path 1 m in front of it.  So the location can immediately take navigation paths again.
            Actor_TeleportToNavMeshClear(parent);
        }

        public void FixCurrentProblem(ActorDetectProblemAndFix parent, ActorDetectFixType fix)
        {
            // If no problem, nothing to fix
            if (!IsProblem) return;

            // If we are debugging the detect problem functions, and dont want the fixes applied yet
            if (parent.DebugOnlyDetectProblemsDontFix)
            {
                Debug.Log($"DEBUG: Problem Detected to Fix, but not fixing: {Actor.Name}  Problem: {ProblemCurrent}  Fix: {fix}");
                return;
            }

            // Get the position before we fix it, so we can save the Initial and the last before fixed
            Vector3 posBeforeFix = Actor_GetPosition();

            // Apply the fixes
            switch (fix)
            {
                case ActorDetectFixType.DisableAirborneInLocation:
                    FixCurrentProblem_DisableAirborneInLocation(parent);
                    break;
                case ActorDetectFixType.TeleportToSafeGround:
                    FixCurrentProblem_TeleportToSafeGround(parent);
                    break;
                default:
                    LogUtil.Error($"Actor Detect Problem: {Actor.Name}  Unknown fix: {fix}  Problem: {ProblemCurrent}");
                    break;
            }

            // Log this fix to History, so we can inspect it later
            var newHistoryItem = new ActorDetectProblemAndFixActorHistoryItem(this, fix, Time.time, posBeforeFix);
            History.Add(newHistoryItem);

            // Trim the oldest item if we are about our maximum
            if (History.Count > parent.MaxHistoryPerActor) History.RemoveAt(0);

            // Remove this actor from the problem list.  They have been fixed.  We keep the history though
            parent.ProblemSolvedForActor(this);

            // Clear the data
            ResetData();
        }
        #endregion

        // Wrapping these so they are easy to replace, and separated from the logic to make things work.
        #region ACTOR_DATA_FUNCTIONS
        // -- TRULY CUSTOM CODE START --
        private Vector3 Actor_GetPosition()
        {
            return Actor.Actor.transform.position;
        }

        private bool Actor_IsFalling()
        {
            // If character controller says we are falling, and we are going down "Velocity.y < 0"
            return Actor.Actor.Status.IsFalling && Actor.Actor.Status.VelocityVerticalDown;
        }

        private bool Actor_IsGrounded()
        {
            return Actor.Actor.Status.IsGrounded;
        }

        private bool Actor_IsAirborne()
        {
            return !Actor.Actor.Status.IsGrounded;
        }

        private float Actor_GetDistanceToGround()
        {
            return Actor.Actor.GetGroundDistance();
        }

        private bool Actor_HasInputNotZero()
        {
            // Is this the player?  Input is in a different location
            if (Actor.IsPlayer)
            {
                if (Actor.InputData.MoveXZ != Vector2.zero) return true;
                else return false;
            }
            // Else, this is an actor, so check animation moveXZ
            else
            {
                //TODO(g): Sync this with Player's use of InputData?  I think it's a good idea.  Even just as an easy place to look...  Copy from AiAnim to Input
                if (Actor.AI.System.Animation.MoveXZ != Vector2.zero) return true;
                else return false;
            }
        }

        [Button("FIX: Snap to Ground")]             //NOTE(g): Can test fixes independently like this
        private void Actor_SnapToGround()
        {
            // Teleport to ground
            var teleportPos = Actor.Actor.GetGroundHitPos();
            Actor.Actor.Teleport(teleportPos);

            // Turn on walking mode
            Actor.ConfigMaster.Character.SetMovementMode(EasyCharacterMovement.MovementMode.Walking);
        }

        [Button("FIX: Teleport to Clear Area")]     //NOTE(g): Can test fixes independently like this
        private void Actor_TeleportToNavMeshClear(ActorDetectProblemAndFix parent = null)
        {
            if (parent == null) parent = Actor.Parent.DectectProblemAndFix;   //NOTE: This will be NULL for Button use, so get parent from the hierarchy

            // Pass in our position, but we could use a different position than our own.  Like a nearby NavPath target
            var goodPos = Actor.Actor.FindNearNavMeshClearPos(Actor.Actor.transform.position, parent.TestClearNavPathRadius, parent.TestNavPathForwardDistance);

            // If we could find a good location nearby
            if (goodPos != new Vector3(float.MinValue, float.MinValue, float.MinValue))
            {
                // Teleport them to good position, and we are fixed
                Actor.Actor.Teleport(goodPos);
            }

            // Else, we cant, find a good location nearby... And this is the Player
            else if (Actor.IsPlayer)
            {
                // Find nearby NavPath point.  These are the path points around the Market/etc.  Look through the open stages for them.
                goodPos = Actor.Actor.FindNearestLocation(true, parent.TestClearNavPathRadius, parent.TestNavPathForwardDistance);

                // If we dont a good nav path point.  We dont always have this indoors.
                if (goodPos != new Vector3(float.MinValue, float.MinValue, float.MinValue))
                {
                    Actor.Actor.Teleport(goodPos);
                }

                // Else, we couldnt find a nearby navpath point, try to see if they saved leaving a door.  Will still be in same Building or Exterior
                else
                {
                    // If we have our last door, just use this
                    if (Actor.Actor.DoorLastUsed != null)
                    {
                        Actor.Actor.Teleport(Actor.Actor.DoorLastUsed.GetExit());
                    }
                    // Else, we dont have a last door, so find the closest building door.  This one could cause some problems, such as go to a place you couldnt reach before
                    //TODO:FIX: This teleport could lead to story or adventure-gate inconsistency
                    else
                    {
                        // Always get this.  Something will be closest.
                        var door = Actor.Actor.GetClosestBuildingDoor();
                        Actor.Actor.Teleport(door.GetExit());
                    }
                }
            }

            // Else, we cant, find a good location nearby... And this is NOT the Player
            else if (!Actor.IsPlayer)
            {
                // Just disable them, and let spawning figure it out later.  The AI will turn them back on and put them in a good place.
                //      Im teleporting them anyway, but not in view.  It's going to look jank, but better than them staying stuck, until I think of something better.
                Actor.gameObject.SetActive(false);
            }
        }
        // -- TRULY CUSTOM CODE END --
        #endregion

        public void ProcessUpdate(ActorDetectProblemAndFix parent)
        {
            // Determine our current problem
            DetermineProblem(parent);

            // Determine if it is time to fix it (duration limit reached), then fix it
            DetermineFix(parent);
        }
    }

    //PURPOSE: Detect and Fix problems that Actors can have.  This keeps this logic out of normal Actor AI/etc, so they can purely work under assumed good conditions.
    //          This will fix the bad conditions, so that they are good again, and normal operations can be simplified by removing error checking.  Otherwise hard over time.
    public class ActorDetectProblemAndFix : MonoBehaviour
    {
        #region FIELDS
        public GameMaster Game;

        [Tooltip("The player is special, as we always want to process them")]
        public ActorDetectProblemAndFixActor Player;

        [Tooltip("DEBUG: Only detect problems, dont fix.  So you can test your detection of problems is correct without your fixes changing things quickly.")]
        public bool DebugOnlyDetectProblemsDontFix = false;

        [Tooltip("Keep track of every actor, so we can determine if they have problem.")]
        public List<ActorDetectProblemAndFixActor> Actors = new List<ActorDetectProblemAndFixActor>();

        [Tooltip("Short list of problem actors to cehck every frame until fixed, like the player.")]
        public List<ActorDetectProblemAndFixActor> ProblemActors = new List<ActorDetectProblemAndFixActor>();

        [Tooltip("These need to be removed.  Track because of foreach iteration")]
        public List<ActorDetectProblemAndFixActor> RemoveProblemActors = new List<ActorDetectProblemAndFixActor>();

        [Tooltip("How many history entries to keep?  Beyond this it's too hard to read anyway, so trim it")]
        public int MaxHistoryPerActor = 10;

        [Tooltip("How long until we should search for problem actors again?")]
        public float TimeSearchedDelay = 1.7f;
        [Tooltip("When did we last search?")]
        public float TimeSearchedLast;

        [Tooltip("How long until airborne is a problem?")]
        public float MaxTimeAirborne = 4f;
        [Tooltip("How long until falling is a problem?")]
        public float MaxTimeFalling = 3f;
        [Tooltip("Movement Input is being recevied, but there is not any position change.  Even with wall, there should be sliding.  Theyd have to get the angle perfect and keep it.  Test")]
        public float MaxTimeNotMovingWithInputMove = 2f;    // What if they just press into a wall?  Thats valid and would trigger this...  Make more tests inside...

        [Tooltip("How long until we fix after recognizing the problem:  Airborne")]
        public float DurationToFixAirborne = 1f;
        [Tooltip("How long until we fix after recognizing the problem:  Falling")]
        public float DurationToFixFalling = 1f;
        [Tooltip("How long until we fix after recognizing the problem:  Not moving with input")]
        public float DurationToFixNotMovingWithInputMove = 1f;

        [Tooltip("How far in a radius should be test a position to see if its clear to navigation to-from?")]
        public float TestClearNavPathRadius = 1.2f;
        public float TestNavPathForwardDistance = 1f;
        #endregion

        [Button("Configure")]
        public void Configure(bool resetSearch = false)
        {
            // If resetSearch==true, set it so we never searched before
            if (resetSearch) TimeSearchedLast = float.MinValue;

            // If we dont have the player, set them up
            //TODO(g): Multiplayer is just a list, but dont have them now, so skipping for perf
            if (Player == null || Player.Actor == null) Player = new ActorDetectProblemAndFixActor(Game.PlayerData);

            // Clear our actors and problem actors, so we can start over.  We lose any history, etc.  At start, or if actors totally change.
            //NOTE(g): Dynamic spawns can be reset individually.
            Actors = new List<ActorDetectProblemAndFixActor>();
            ProblemActors = new List<ActorDetectProblemAndFixActor>();

            // Add all the actors, so we can review them with native problem-fix data.  Keeping this separate from other systems.  Overwatch.
            foreach (var actorData in Game.ActorDataManager.Actors)
            {
                var actor = new ActorDetectProblemAndFixActor(actorData);
                Actors.Add(actor);
            }
        }

        [Button("Search for Problem Actors")]
        private void SearchForProblemActors(bool force = false)
        {
            // Force this search if it is our first time
            if (TimeSearchedLast == float.MinValue) force = true;

            // Only search for problem actors every N seconds, so that we dont need to search constantly.  Delay test.
            if (!force && TimeSearchedLast + TimeSearchedDelay < Time.time) return;

            // Check for any actors we dont know of that currently have problems
            foreach (var actor in Actors)
            {
                // Skip if its the player, or already a problem actor, or it is deactivated so we dont need to think about it having problems now
                if (actor.Actor.IsPlayer || ProblemActors.Contains(actor) || !actor.Actor.IsActive) continue;

                // Determine if they have a problem, if they do, add them to the problem actors to check every frame until fixed
                bool hasProblem = actor.DetermineProblem(this);
                if (hasProblem) ProblemActors.Add(actor);
            }

            // Mark time we searched, so we can delay again
            TimeSearchedLast = Time.time;
        }

        public void ProblemSolvedForActor(ActorDetectProblemAndFixActor actor)
        {
            // Remove this actor from the ProblemActors, so we dont check them every frame.  Their problem was fixed.
            //      Remove it later, because we are iterating on it now.
            RemoveProblemActors.Add(actor);
        }

        //PURPOSE: New dynamically spawned actor takes over existing ActorData, need fresh information here
        public void ActorResetData(ActorData actorData)
        {
            // Find the actor requested, and reset their data.  Maybe they just dynamically spawned and are essentially a new actor now.  Should have new history.
            foreach (var actor in Actors)
            {
                if (actor.Actor.UUID == actorData.UUID)
                {
                    actor.ResetData();
                    return;
                }
            }
        }

        public float GetDurationToFix(ActorDetectProblemType problem)
        {
            // Get our duration to fix
            switch (problem)
            {
                case ActorDetectProblemType.AirborneTooLong: return DurationToFixAirborne;
                case ActorDetectProblemType.FallingTooLong: return DurationToFixFalling;
                case ActorDetectProblemType.HasInputButNoMovement: return DurationToFixNotMovingWithInputMove;
                default:
                    LogUtil.Error($"Could not find duration for problem.  Broken, will never fix.  Add this: {problem}");
                    return float.MaxValue;
            }
        }

        public float GetMaxTimeBeforeProblem(ActorDetectProblemType problemMaybe)
        {
            // Get maximum time before this becomes a problem
            switch (problemMaybe)
            {
                case ActorDetectProblemType.AirborneTooLong: return MaxTimeAirborne;
                case ActorDetectProblemType.FallingTooLong: return MaxTimeFalling;
                case ActorDetectProblemType.HasInputButNoMovement: return MaxTimeNotMovingWithInputMove;
                default:
                    LogUtil.Error($"Could not find max time to become a problem.  Broken, will never fix.  Add this: {problemMaybe}");
                    return float.MaxValue;
            }
        }

        #region START_AND_UPDATE
        private void Start()
        {
            // Configure actors
            Configure(true);
        }

        public void ProcessUpdate()
        {
            // Look to see if we have new problem cases
            SearchForProblemActors(false);

            // Process the Player every frame, so we give them the best results
            Player.ProcessUpdate(this);

            // Process the Problem Actors every frame, until we can remove them
            RemoveProblemActors.Clear();
            foreach (var problemActor in ProblemActors)
            {
                // If this actor has become inactive, then remove it
                if (!problemActor.Actor.IsActive)
                {
                    problemActor.ResetData();
                    RemoveProblemActors.Add(problemActor);
                    continue;
                }

                problemActor.ProcessUpdate(this);
            }

            // Remove any actors that had their problems fixed, or were disabled, or otherwise are no longer problem actors to check every frame
            foreach (var actor in RemoveProblemActors) if (ProblemActors.Contains(actor)) ProblemActors.Remove(actor);
        }

        private void Update()
        {
            // Dont run this if we are not in play mode, or the game is paused.  No point.
            //NOTE(g): May need to update times when we come out of pause, or get a large delta in case Time.time is still not frozen during pause.
            if (Game.UI.StateCurrent != UIMasterState.Play || Game.IsPaused()) return;

            // Detect problems and fix them
            ProcessUpdate();
        }
        #endregion
    }
}

