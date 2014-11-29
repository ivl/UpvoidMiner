// Copyright (C) by Upvoid Studios
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>

using System;
using Engine;
using Engine.Audio;
using Engine.Universe;
using Engine.Physics;
using Engine.Input;
using Engine.Resources;
using Engine.Scripting;
using Engine.Webserver;
using Engine.Rendering;
using UpvoidMiner.UI;

namespace UpvoidMiner
{
    /// <summary>
    /// A simple character controller.
    /// It takes control of a rigid body and lets the user steer it around using the WASD keys.
    /// A given camera is used to get the walking direction.
    /// </summary>
    public class CharacterController
    {
        /// <summary>
        /// The rigid body that represents the controlled character to the physics system.
        /// </summary>
        public RigidBody Body { get; protected set; }

        /// <summary>
        /// The current position of the controlled character.
        /// </summary>
        public vec3 Position
        {
            get
            {
                return new vec3(Body.GetTransformation().col3);
            }
        }
        /// <summary>
        /// Gets the transformation matrix of the controlled character.
        /// </summary>
        public mat4 Transformation
        {
            get
            {
                return Body.GetTransformation();
            }
        }

        /// <summary>
        /// If true, the player is not pulled down to the ground and can fly around.
        /// Also, all body size settings are ignored; the player's body is a small sphere around the camera.
        /// </summary>
        public bool GodMode { get; protected set; }

        /// <summary>
        /// The height that the controller tries to keep between the body and the ground.
        /// The RigidBody representing the character hovers above the ground to make walking on non-planar ground easier.
        /// </summary>
        /// <value>
        /// Sane values usually lie between 0.2 and 0.5. Default is 0.4.
        /// If it is too low, the body will collide with obstacles that a real person would just step over.
        /// If it is too high, the body will not collide with obstacles that a real person would not simply step over.
        /// </value>
        public float HoverHeight = 0.3f;

        /// <summary>
        /// The total height of the simulated character (from the ground to the top, including HoverHeight).
        /// </summary>
        public float CharacterHeight { get; protected set; }

        /// <summary>
        /// The height of the body, equals CharacterHeight - HoverHeight.
        /// </summary>
        public float BodyHeight { get { return CharacterHeight - HoverHeight; } }

        /// <summary>
        /// The y offset from Position to the position of the character's eyes. This assumes the eyes are 10cm below the character's top. Can be used to position a camera.
        /// </summary>
        public float EyeOffset { get { return 0.5f * BodyHeight - 0.1f; } }

        /// <summary>
        /// The diameter of the character's body.
        /// </summary>
        public float CharacterDiameter { get; protected set; }

        /// <summary>
        /// The mass of the character's body in kilograms.
        /// </summary>
        public float CharacterMass { get { return Body.Mass; } }

        /// <summary>
        /// The physical impulse (meters per second) that will be applied to the body for a jump.
        /// </summary>
        public float JumpImpulse = 300f;

        /// <summary>
        /// The velocity of the character when walking (meters per second). Default is 2.7.
        /// </summary>
        public float WalkSpeed = 2.7f;

        /// <summary>
        /// The velocity of the character when strafing (meters per second). Default is 1.0 (3.6 km/h).
        /// </summary>
        public float StrafeSpeed = 2f;

        /// <summary>
        /// The velocity of the character when strafing while running (meters per second). Default is 3.0 (11 km/h).
        /// </summary>
        public float StrafeSpeedRunning = 4.5f;

        /// <summary>
        /// The velocity of the character when running (meters per second). Default is 6.
        /// </summary>
        public float WalkSpeedRunning = 6f;

        /// <summary>
        /// Returns true iff the character is currently walking or running.
        /// </summary>
        public bool IsWalking { get { return walkDirRight != 0 || walkDirForward != 0; } }

        /// <summary>
        /// True iff the character is currently running.
        /// </summary>
        public bool IsRunning { get; protected set; }

        /// <summary>
        /// True iff the character is closer than 40cm to the ground. Usually, it hovers 30cm above.
        /// </summary>
        public bool TouchesGround { get; protected set; }

        /// <summary>
        /// The world that contains the controlled rigid body.
        /// </summary>
        public World ContainingWorld { get; protected set; }

        /// <summary>
        /// This camera is used to determine the directions we are walking. Forward means the direction the camera is currently pointing.
        /// </summary>
        GenericCamera camera;

        /// <summary>
        /// Forward/neutral/backward encoded in -1/0/1
        /// </summary>
        int walkDirForward = 0;

        /// <summary>
        /// Left/neutral/right encoded in -1/0/1
        /// </summary>
        int walkDirRight = 0;

        /// <summary>
        /// The last known distance to the ground.
        /// </summary>
        float distanceToGround = 0;

        float jumpCoolDown = 0f;


        SoundResource movementNoiseResource;
        readonly Sound movementNoiseSound;

        public CharacterController(GenericCamera _camera, World _containingWorld, bool _godMode = false, float _characterHeight = 1.85f, float _bodyDiameter = 0.45f, float _bodyMass = 70f)
        {
            GodMode = _godMode;
            if (GodMode)
                HoverHeight = 0f;

            camera = _camera;
            ContainingWorld = _containingWorld;

            CharacterHeight = _characterHeight;
            CharacterDiameter = _bodyDiameter;

            // Initialize default values for auto properties
            IsRunning = false;
            WalkSpeed = 2.7f;
            WalkSpeedRunning = 6f;

            // faster if godmode
            if (GodMode)
            {
                WalkSpeed *= 3;
                WalkSpeedRunning *= 3;
            }
            else
            {
                WalkSpeed *= 1.5f;
                WalkSpeedRunning *= 1.5f;
            }

            // Create a capsule shaped rigid body representing the character in the physics world.
            if (GodMode)
                Body = new RigidBody(10000.0f, mat4.Identity, new SphereShape(0.3f));
            else
                Body = new RigidBody(_bodyMass, mat4.Identity, new CapsuleShape(CharacterDiameter / 2f, BodyHeight));
            ContainingWorld.Physics.AddRigidBody(Body);

            // Prevent the rigid body from falling to the ground by simply disabling any rotation
            Body.SetAngularFactor(vec3.Zero);

            // Prevent bouncing
            Body.SetRestitution(0f);

            // This event handler is used to catch the keyboard input that steers the character.
            Input.OnPressInput += HandleInput;

            // Create sound resource for movement noise
            movementNoiseResource = Resources.UseSound("Mods/Upvoid/Resources.SFX/1.0.0::Movement/WalkingOnLeaves", UpvoidMiner.ModDomain);
            movementNoiseSound = new Sound(movementNoiseResource, vec3.Zero, true, 0.75f, 1, (int)AudioType.SFX, true);
        }

        /// <summary>
        /// Called by the player in regular timesteps. Updates the position of the character.
        /// </summary>
        /// <param name="_elapsedSeconds">The elapsed seconds since the last call.</param>
        public void Update(float _elapsedSeconds)
        {

            // When falling, clamp maximum player speed to 55m/s (air friction)
            if(!GodMode && !TouchesGround)
            {
                const float maxSpeed = 55.0f;
                vec3 curVel = Body.GetVelocity();
                float speed = curVel.Length;
                if (speed > maxSpeed)
                {
                    Body.SetVelocity(maxSpeed * curVel.Normalized);
                }
            }

            // Movement noise
            if (TouchesGround && Body.GetVelocity().LengthSqr > 0.1f && !GodMode)
            {
                // Resume movement noise (This is a no-op if sound is already playing)
                movementNoiseSound.Resume();
                movementNoiseSound.Position = camera.Position + new vec3(0, -2, 0);
            }
            else
            {
                // Pause movement noise
                movementNoiseSound.Pause();
            }

            // Don't do anything when noclip is enabled
            if (LocalScript.NoclipEnabled)
            {
                walkDirRight = 0;
                walkDirForward = 0;
                IsRunning = false;
                Body.SetVelocity(vec3.Zero);
                Body.SetGravity(new vec3(0, -9.807f, 0));
                return;
            }
            else if (GodMode)
            {
                Body.SetGravity(vec3.Zero);
            }

            // Security: if in non-air chunk, teleport to next all-air one
            {
                mat4 transformation = Body.GetTransformation();
                vec3 pos = new vec3(transformation.col3);
                WorldTreeNode node = ContainingWorld.QueryWorldTreeNode(pos);
                if (node != null && node.IsMinLod)
                {
                    HermiteData volumeData = node.CurrentVolume;
                    if (volumeData != null)
                    {
                        if (!volumeData.HasAir)
                        {
                            // we are definitely in a non-air chunk here
                            // teleport one node size above
                            Body.SetTransformation(mat4.Translate(new vec3(0, node.Size, 0)) * Body.GetTransformation());
                        }
                        else if (!volumeData.HasAirAt(pos))
                        {
                            // we are in a mixed chunk, advance pos until air
                            float offset = 0f;
                            do
                            {
                                pos.y += 0.5f;
                                offset += 0.5f;
                            } while (!volumeData.HasAirAt(pos) || !volumeData.HasAirAt(pos + new vec3(0, 1.5f, 0)));

                            // another 1.5m to ensure good ground
                            offset += 1.5f;

                            Body.SetTransformation(mat4.Translate(new vec3(0, offset, 0)) * Body.GetTransformation());
                        }
                    }
                }
            }

            jumpCoolDown -= _elapsedSeconds;

            // Jumping cooldown is reset instantly when moving down in any way.
            if (Body.GetVelocity().y <= 0f)
                jumpCoolDown = 0f;

            if (jumpCoolDown < 0f)
                jumpCoolDown = 0f;

            // When touching the ground, we can walk around with full control over our velocity. In Godmode, we can always 'walk'.
            if ((TouchesGround && jumpCoolDown <= 0f) || GodMode)
            {

                float forwardSpeed = IsRunning ? WalkSpeedRunning : WalkSpeed;
                float strafeSpeed = IsRunning ? StrafeSpeedRunning : StrafeSpeed;

                // Use the forward and right directions of the camera. When not in god mode, remove the y component, and we have our walking direction.
                vec3 moveDir = camera.ForwardDirection * walkDirForward * forwardSpeed + camera.RightDirection * walkDirRight * strafeSpeed;
                vec3 velocity = Body.GetVelocity();

                if (!GodMode)
                {
                    moveDir.y = 0;
                    velocity.y = 0;
                }

                Body.ApplyImpulse((moveDir - velocity) * CharacterMass, vec3.Zero);

                Tutorials.MsgIntro.Report(moveDir.Length * _elapsedSeconds);
                if (IsRunning) Tutorials.MsgMovementSprint.Report(moveDir.Length * _elapsedSeconds);
            }
            else // Otherwise, we can do some subtile acceleration in air
            {
                float forwardSpeed = StrafeSpeed * 0.25f;
                float strafeSpeed = StrafeSpeed * 0.25f;

                // Use the forward and right directions of the camera. Remove the y component, and we have our walking direction.
                vec3 moveDir = camera.ForwardDirection * walkDirForward * forwardSpeed + camera.RightDirection * walkDirRight * strafeSpeed;
                moveDir.y = 0;

                vec3 oldVelocity = Body.GetVelocity();
                oldVelocity.y = 0;

                float oldVelocityLength = oldVelocity.Length;
                vec3 newVelocity = oldVelocity + moveDir;
                float newVelocityLength = newVelocity.Length;
                if (oldVelocityLength > 1f && newVelocityLength > oldVelocityLength)
                    newVelocity *= oldVelocityLength / newVelocityLength;

                newVelocity.y = Body.GetVelocity().y;

                Body.ApplyImpulse((newVelocity - Body.GetVelocity()) * CharacterMass, vec3.Zero);

                Tutorials.MsgIntro.Report(moveDir.Length * _elapsedSeconds);
                if (IsRunning) Tutorials.MsgMovementSprint.Report(moveDir.Length * _elapsedSeconds);
            }

            // Let the character hover over the ground by applying a custom gravity. We apply the custom gravity when the body is below the desired height plus 0.1 meters.
            // Our custom gravity pushes the body to its desired height and becomes smaller the closer it gets to prevent rubber band effects.
            // Also, only tinker with the gravcity if the player is moving relatively slow
            if (!GodMode && distanceToGround < HoverHeight + 0.1f && jumpCoolDown <= 0f && Body.GetVelocity().Length < WalkSpeedRunning * 1.5f)
            {
                vec3 velocity = Body.GetVelocity();

                // Never move down when more than 10cm below the desired height.
                if (distanceToGround < HoverHeight - 0.1f && velocity.y < 0f)
                {
                    Body.ApplyImpulse(Body.Mass * new vec3(0, -velocity.y, 0), vec3.Zero);
                    velocity.y = 0f;
                }

                float convergenceSpeed = Math.Max(0.1f, _elapsedSeconds * 1.2f);
                float distanceToHoverHeight = distanceToGround - HoverHeight;

                float customGravity = -2f * (distanceToHoverHeight + velocity.y * convergenceSpeed) / (convergenceSpeed * convergenceSpeed);

                if (customGravity < -20f)
                    customGravity = -20f;
                else if (customGravity > 20f)
                    customGravity = 20f;

                Body.SetGravity(new vec3(0, customGravity, 0));

            }
            else if (!GodMode)
                Body.SetGravity(new vec3(0, -9.807f, 0));

            // Recalc distance to ground
            if (!GodMode)
            {
                RayHit hit = ContainingWorld.Physics.RayTest(Position, Position - new vec3(0, 500f, 0), Body);

                if (hit != null)
                {
                    distanceToGround = Position.y - BodyHeight * 0.5f - hit.Position.y;
                }
                else
                    distanceToGround = 5f;

                TouchesGround = (jumpCoolDown <= 0f) && (Math.Abs(distanceToGround) < HoverHeight + 0.3f);
            }
        }

        /// <summary>
        /// Called on keyboard input. Updates the walking directions of the character.
        /// </summary>
        protected void HandleInput(object sender, InputPressArgs e)
        {
            if (!Rendering.MainViewport.HasFocus)
                return;
            if (LocalScript.NoclipEnabled)
                return;


            // Do not walk around when menu or inventory is open
            bool menuOrInventoryOpen = LocalScript.player.Gui.IsInventoryOpen || LocalScript.player.Gui.IsMenuOpen;
            if (!menuOrInventoryOpen)
            {
                if (e.Key == InputKey.F && e.PressType == InputPressArgs.KeyPressType.Down)
                {
                    Body.SetVelocity(vec3.Zero);
                    mat4 transformation = Body.GetTransformation();
                    vec3 pos = new vec3(transformation.col3);
                    pos.y += 20f;
                    Body.SetTransformation(mat4.Translate(pos));
                }

                // Let the default WASD-keys control the walking directions.
                if (e.Key == InputKey.W)
                {
                    if (e.PressType == InputPressArgs.KeyPressType.Down)
                        walkDirForward++;
                    else
                    {
                        walkDirForward--;

                        // Releasing the forward-key shall not result in moving backwards.
                        if (walkDirForward < 0)
                            walkDirForward = 0;
                    }

                }
                else if (e.Key == InputKey.S)
                {
                    if (e.PressType == InputPressArgs.KeyPressType.Down)
                        walkDirForward--;
                    else
                    {
                        walkDirForward++;

                        // Releasing the backward-key shall not result in moving forwards.
                        if (walkDirForward > 0)
                            walkDirForward = 0;
                    }
                }
                else if (e.Key == InputKey.D)
                {
                    if (e.PressType == InputPressArgs.KeyPressType.Down)
                        walkDirRight++;
                    else
                    {
                        walkDirRight--;

                        // Releasing the strafe-right-key shall not result in strafing left.
                        if (walkDirRight < 0)
                            walkDirRight = 0;
                    }
                }
                else if (e.Key == InputKey.A)
                {
                    if (e.PressType == InputPressArgs.KeyPressType.Down)
                        walkDirRight--;
                    else
                    {
                        walkDirRight++;

                        // Releasing the strafe-left-key shall not result in strafing right.
                        if (walkDirRight > 0)
                            walkDirRight = 0;
                    }
                }
                else if (e.Key == InputKey.Space && e.PressType == InputPressArgs.KeyPressType.Down)
                { //Space lets the player jump
                    if (!GodMode && TouchesGround && jumpCoolDown == 0f)
                    {
                        Body.ApplyImpulse(new vec3(0, 5f * CharacterMass, 0), vec3.Zero);
                        jumpCoolDown = 0.5f;
                        Tutorials.MsgMovementJump.Report(1);
                    }
                }
                else if (e.Key == InputKey.Shift)
                { // Shift controls running
                    if (e.PressType == InputPressArgs.KeyPressType.Down)
                        IsRunning = true;
                    else
                        IsRunning = false;
                }
                else if (e.Key == InputKey.O)
                {
                    Body.SetTransformation(mat4.Translate(new vec3(0, 50f, 0)) * Body.GetTransformation());
                    Body.SetVelocity(vec3.Zero);
                }

                // Clamp the walking directions to [-1, 1]. The values could get out of bound, for example, when we receive two down events without an up event in between.
                if (walkDirForward < -1)
                    walkDirForward = -1;
                if (walkDirForward > 1)
                    walkDirForward = 1;
                if (walkDirRight < -1)
                    walkDirRight = -1;
                if (walkDirRight > 1)
                    walkDirRight = 1;
            }
            else
            {
                // Menu was opened, stop walking.
                walkDirForward = walkDirRight = 0;
            }


            // This hack stops the player movement immediately when we stop walking
            //TODO: do some actual friction simulation instead
            if (walkDirRight == 0 && walkDirForward == 0 && TouchesGround && e.PressType == InputPressArgs.KeyPressType.Up)
            {
                Body.SetVelocity(vec3.Zero);
            }
        }
    }
}

