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
using System.Diagnostics;
using Engine;
using Engine.Universe;
using Engine.Rendering;
using Engine.Resources;
using Engine.Physics;
using UpvoidMiner.Items;
using UpvoidMiner.UI;

namespace UpvoidMiner
{
    /// <summary>
    /// Shape of a material item.
    /// </summary>
    public enum MaterialShape
    {
        Cube,
        Cylinder,
        Sphere,
        Cone
    }

    /// <summary>
    /// An item that is a material instance of a given resource.
    /// E.g. an Iron Sphere
    /// </summary>
    public class MaterialItem : DiscreteItem
    {
        /// <summary>
        /// The resource that his item is made of.
        /// </summary>
        public readonly Substance Substance;
        /// <summary>
        /// The shape of this item.
        /// </summary>
        public readonly MaterialShape Shape;
        /// <summary>
        /// Size of this item:
        /// Cube: width, height, depth
        /// Cylinder: radius, height, radius
        /// Sphere: radius, (y/z = radius)
        /// Cone: radius, height, radius
        /// </summary>
        public readonly vec3 Size;
        
        /// <summary>
        /// Volume of this material item.
        /// </summary>
        public float Volume { get { return VolumeOf(Shape, Size); } }

        /// <summary>
        /// Gets a textual description of the dimensions
        /// </summary>
        public string DimensionString
        {
            get
            {
                switch (Shape)
                {
                    case MaterialShape.Cube: return "size " + Size.x.ToString("0.0") + " m x " + Size.y.ToString("0.0") + " m x " + Size.z.ToString("0.0") + " m";
                    case MaterialShape.Cylinder: return Size.x.ToString("0.0") + " m radius and " + Size.y.ToString("0.0") + " m height";
                    case MaterialShape.Sphere: return Size.x.ToString("0.0") + " m radius";
                    case MaterialShape.Cone: return Size.x.ToString("0.0") + " m radius and " + Size.y.ToString("0.0") + " m height";
                    default: Debug.Fail("Invalid shape"); return "<invalid>";
                }
            }
        }

        /// <summary>
        /// Returns the volume of a given shape/size combo
        /// </summary>
        public static float VolumeOf(MaterialShape shape, vec3 size)
        {
            switch (shape)
            {
                case MaterialShape.Cube: return size.x * size.y * size.z;
                case MaterialShape.Cylinder: return 2 * (float)Math.PI * size.x * size.y * size.z;
                case MaterialShape.Sphere: return 4f / 3f * (float)Math.PI * size.x * size.y * size.z;
                case MaterialShape.Cone: return 1f / 3f * (float)Math.PI * size.x * size.y * size.z;
                default: Debug.Fail("Invalid shape"); return -1;
            }
        }

        public MaterialItem(Substance substance, MaterialShape shape, vec3 size, int stackSize = 1) :
            base(substance.Name + " " + shape, null, substance.MassDensity * VolumeOf(shape, size), ItemCategory.Material, stackSize)
        {
            Substance = substance;
            Shape = shape;
            Size = size;
            Description = "A " + shape + " made of " + substance.Name + " with " + DimensionString;
            Icon = "Substances/" + substance.Name + "," + shape;
            IsDroppable = true;
        }

        /// <summary>
        /// This can be merged with material items of the same resource and shape and size.
        /// </summary>
        public override bool TryMerge(Item rhs, bool subtract, bool force, bool dryrun = false)
        {
            MaterialItem item = rhs as MaterialItem;
            if (item == null) return false;
            if (!subtract && !Substance.GetType().IsInstanceOfType(item.Substance)) return false;
            if (subtract && !item.Substance.GetType().IsInstanceOfType(Substance)) return false;
            if (item.Shape != Shape) return false;
            if (item.Size != Size) return false;

            return Merge(item, subtract, force, dryrun);
        }

        /// <summary>
        /// Creates a copy of this item.
        /// </summary>
        public override Item Clone()
        {
            return new MaterialItem(Substance, Shape, Size, StackSize);
        }

        public override Item Clone(Substance sub)
        {
            return new MaterialItem(sub, Shape, Size, StackSize);
        }

        #region Inventory Logic
        /// <summary>
        /// Renderjob for the preview material
        /// </summary>
        private MeshRenderJob previewMaterialPlaced;
        private MeshRenderJob previewMaterialPlacedIndicator;
        private MeshRenderJob materialAlignmentGrid;
        private RenderComponent materialAlignmentGridRenderComp;
        private bool previewPlacable = false;
        private mat4 previewPlaceMatrix;

        /// <summary>
        /// Yes, we have a preview for materials (ray for placement, update for holding).
        /// </summary>
        public override bool HasRayPreview { get { return true; } }
        public override bool HasUpdatePreview { get { return true; } }

        public override void OnUse(Player player, vec3 _worldPos, vec3 _worldNormal, Entity _hitEntity)
        {
            if (!previewPlacable || player == null)
                return;

            Item droppedItem = new MaterialItem(Substance, Shape, Size);

            switch (player.CurrentPhysicsMode)
            {
                case DiggingController.PhysicsMode.Dynamic:
                    if (!player.GodMode) // don't remove in godmode
                        player.Inventory.RemoveItem(droppedItem);
                    ItemManager.InstantiateItem(droppedItem, previewPlaceMatrix, false);
                    break;

                case DiggingController.PhysicsMode.Static:
                    if (!player.GodMode) // don't remove in godmode
                        player.Inventory.RemoveItem(droppedItem);
                    ItemManager.InstantiateItem(droppedItem, previewPlaceMatrix, true);

                    // Tutorial
                    Tutorials.MsgAdvancedCraftingStaticUse.Report(1);
                    break;

                case DiggingController.PhysicsMode.Thrown:
                    player.DropItem(droppedItem, _worldPos);

                    // Tutorial
                    Tutorials.MsgAdvancedCraftingThrowUse.Report(1);
                    break;
            }

            // Tutorial
            if (Shape == MaterialShape.Cube && Substance is DirtSubstance)
                Tutorials.MsgBasicCraftingDirtCubePlace.Report(1);
        }

        public override void OnSelect(Player player)
        {
            MeshResource mesh;
            switch (Shape)
            {
                case MaterialShape.Cube: mesh = Resources.UseMesh("::Debug/Box", UpvoidMiner.ModDomain); break;
                case MaterialShape.Sphere: mesh = Resources.UseMesh("::Debug/Sphere", UpvoidMiner.ModDomain); break;
                case MaterialShape.Cylinder: mesh = Resources.UseMesh("::Debug/Cylinder", UpvoidMiner.ModDomain); break;
                default: throw new NotImplementedException("Invalid shape");
            }

            // Create an overlay object as 'placement-indicator'.
            previewMaterialPlaced = new MeshRenderJob(Renderer.Overlay.Mesh, Resources.UseMaterial("Items/ResourcePreview", UpvoidMiner.ModDomain), mesh, mat4.Scale(0f));
            LocalScript.world.AddRenderJob(previewMaterialPlaced);
            // And a second one for indicating the center.
            previewMaterialPlacedIndicator = new MeshRenderJob(Renderer.Overlay.Mesh, Resources.UseMaterial("Items/ResourcePreviewIndicator", UpvoidMiner.ModDomain), Resources.UseMesh("::Debug/Sphere", null), mat4.Scale(0f));
            LocalScript.world.AddRenderJob(previewMaterialPlacedIndicator);
            // And a third one for the alignment grid.
            materialAlignmentGrid = new MeshRenderJob(Renderer.Overlay.Mesh, Resources.UseMaterial("Items/GridAlignment", UpvoidMiner.ModDomain), Resources.UseMesh("Triplequad", UpvoidMiner.ModDomain), mat4.Scale(0f));
            materialAlignmentGridRenderComp = new RenderComponent(materialAlignmentGrid, mat4.Identity);
            LocalScript.ShapeIndicatorEntity.AddComponent(materialAlignmentGridRenderComp);
        }

        public override void OnUseParameterChange(Player player, float _delta)
        {
            // TODO: maybe rotate?
        }

        public override void OnRayPreview(Player _player, RayHit rayHit, CrosshairInfo crosshair)
        {
            // Hide if not visible.
            if (rayHit == null)
            {
                previewMaterialPlaced.ModelMatrix = mat4.Scale(0f);
                previewMaterialPlacedIndicator.ModelMatrix = mat4.Scale(0f);
                previewPlacable = false;
                crosshair.Disabled = true;
                return;
            }

            vec3 dir = _player.CameraDirection;
            vec3 up = rayHit.Normal;
            vec3 left = vec3.cross(up, dir).Normalized;
            dir = vec3.cross(left, up);

            var _worldPos = rayHit == null ? vec3.Zero : rayHit.Position + rayHit.Normal.Normalized * (0.01f / 7f) /* small security offset */;
            var savPos = _worldPos;

            mat4 scaling;
            float offset;
            switch (Shape)
            {
                case MaterialShape.Cube:
                    scaling = mat4.Scale(Size / 2f);
                    offset = Size.y / 2f;
                    break;
                case MaterialShape.Sphere:
                    scaling = mat4.Scale(Size);
                    offset = Size.y;
                    break;
                case MaterialShape.Cylinder:
                    scaling = mat4.Scale(new vec3(Size.x, Size.y / 2f, Size.z));
                    offset = Size.y / 2f;
                    break;
                default: throw new NotImplementedException("Invalid shape");
            }

            vec3 dx, dy, dz;
            _player.AlignmentSystem(up, out dx, out dy, out dz);
            mat4 rotMat = new mat4(dx, dy, dz, vec3.Zero);
            _worldPos = _player.AlignPlacementPosition(_worldPos, up, offset);
            
            previewPlacable = true;
            previewPlaceMatrix = mat4.Translate(_worldPos) * rotMat;

            materialAlignmentGrid.SetColor("uMidPointAndRadius", new vec4(_worldPos, _player.DiggingGridSize / 2.0f));
            materialAlignmentGrid.SetColor("uCursorPos", new vec4(savPos, 0));
            materialAlignmentGrid.SetColor("uTerrainNormal", new vec4(up, 0));
            materialAlignmentGrid.SetColor("uDigDirX", new vec4(dx, 0));
            materialAlignmentGrid.SetColor("uDigDirY", new vec4(dy, 0));
            materialAlignmentGrid.SetColor("uDigDirZ", new vec4(dz, 0));

            bool gridAlignmentVisible = _player.CurrentDiggingAlignment == DiggingController.DigAlignment.GridAligned;
            materialAlignmentGridRenderComp.Transform = gridAlignmentVisible ? mat4.Translate(_worldPos) * mat4.Scale(2f * _player.DiggingGridSize) * rotMat : mat4.Scale(0f);


            // The placed object is scaled accordingly
            previewMaterialPlaced.ModelMatrix = previewPlaceMatrix * scaling;
            // Indicator is always in the center and relatively small.
            previewMaterialPlacedIndicator.ModelMatrix = mat4.Translate(rayHit.Position) * mat4.Scale(.1f);
        }

        public override void OnUpdatePreview(Player _player, float _elapsedSeconds, CrosshairInfo crosshair)
        {
        }

        public override void OnDeselect(Player player)
        {
            // Remove and delete it on deselect.
            LocalScript.world.RemoveRenderJob(previewMaterialPlaced);
            LocalScript.world.RemoveRenderJob(previewMaterialPlacedIndicator);
            LocalScript.ShapeIndicatorEntity.RemoveComponent(materialAlignmentGridRenderComp);
            previewMaterialPlaced = null;
            previewMaterialPlacedIndicator = null;
            materialAlignmentGrid = null;
        }
        #endregion

        #region Item Entity
        /// <summary>
        /// Is called when an entity is created for this item (e.g. if dropped).
        /// This function is supposed to add renderjobs and physicscomponents.
        /// Don't forget to add components to the item entity!
        /// </summary>
        public override void SetupItemEntity(ItemEntity itemEntity, Entity entity, bool fixedPosition = false)
        {
            // Create an appropriate physics shape.
            CollisionShape collShape;
            MeshResource mesh;
            mat4 scaling;
            switch (Shape)
            {
                case MaterialShape.Cube:
                    collShape = new BoxShape(Size / 2f);
                    scaling = mat4.Scale(Size / 2f);
                    mesh = Resources.UseMesh("Box", UpvoidMiner.ModDomain);
                    break;
                case MaterialShape.Sphere:
                    collShape = new SphereShape(Size.x);
                    scaling = mat4.Scale(Size);
                    mesh = Resources.UseMesh("Sphere", UpvoidMiner.ModDomain);
                    break;
                case MaterialShape.Cylinder:
                    collShape = new CylinderShape(Size.x, Size.y / 2f);
                    mesh = Resources.UseMesh("Cylinder", UpvoidMiner.ModDomain);
                    scaling = mat4.Scale(new vec3(Size.x, Size.y / 2f, Size.z));
                    break;
                default: throw new NotImplementedException("Invalid Shape");
            }

            // Create the physical representation of the item.
            RigidBody body = new RigidBody(
                fixedPosition ? 0f : Weight,
                entity.Transform,
                collShape
                );
            body.SetRestitution(0.5f);
            body.SetFriction(1f);
            body.SetDamping(0.2f, 0.4f);
            itemEntity.ContainingWorld.Physics.AddRigidBody(body);

            itemEntity.AddPhysicsComponent(new PhysicsComponent(body, mat4.Identity));

            MaterialResource material;
            var mat = Substance.QueryResource();
            if (mat is SolidTerrainResource)
                material = (mat as SolidTerrainResource).RenderMaterial;
            else
                throw new NotImplementedException("Unknown terrain resource");

            // Create the graphical representation of the item.
            MeshRenderJob renderJob = new MeshRenderJob(
                Renderer.Opaque.Mesh,
                material,
                mesh,
                mat4.Identity
                );
            itemEntity.AddRenderComponent(new RenderComponent(renderJob, scaling, true));

            MeshRenderJob renderJobShadow = new MeshRenderJob(
                Renderer.Shadow.Mesh,
                Resources.UseMaterial("::Shadow", UpvoidMiner.ModDomain),
                mesh,
                mat4.Identity
                );
            itemEntity.AddRenderComponent(new RenderComponent(renderJobShadow, scaling, true));

        }
        #endregion
    }
}

