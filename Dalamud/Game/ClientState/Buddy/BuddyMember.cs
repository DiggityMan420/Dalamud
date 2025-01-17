using System;

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Resolvers;
using JetBrains.Annotations;

namespace Dalamud.Game.ClientState.Buddy
{
    /// <summary>
    /// This class represents a buddy such as the chocobo companion, summoned pets, squadron groups and trust parties.
    /// </summary>
    public unsafe class BuddyMember
    {
        private Dalamud dalamud;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuddyMember"/> class.
        /// </summary>
        /// <param name="address">Buddy address.</param>
        /// <param name="dalamud">Dalamud instance.</param>
        internal BuddyMember(IntPtr address, Dalamud dalamud)
        {
            this.dalamud = dalamud;
            this.Address = address;
        }

        /// <summary>
        /// Gets the address of the buddy in memory.
        /// </summary>
        public IntPtr Address { get; }

        /// <summary>
        /// Gets the object ID of this buddy.
        /// </summary>
        public uint ObjectId => this.Struct->ObjectID;

        /// <summary>
        /// Gets the actor associated with this buddy.
        /// </summary>
        /// <remarks>
        /// This iterates the actor table, it should be used with care.
        /// </remarks>
        [CanBeNull]
        public GameObject Actor => this.dalamud.ClientState.Objects.SearchByID(this.ObjectId);

        /// <summary>
        /// Gets the current health of this buddy.
        /// </summary>
        public uint CurrentHP => this.Struct->CurrentHealth;

        /// <summary>
        /// Gets the maximum health of this buddy.
        /// </summary>
        public uint MaxHP => this.Struct->MaxHealth;

        /// <summary>
        /// Gets the data ID of this buddy.
        /// </summary>
        public uint DataID => this.Struct->DataID;

        /// <summary>
        /// Gets the Mount data related to this buddy. It should only be used with companion buddies.
        /// </summary>
        public ExcelResolver<Lumina.Excel.GeneratedSheets.Mount> MountData => new(this.DataID, this.dalamud);

        /// <summary>
        /// Gets the Pet data related to this buddy. It should only be used with pet buddies.
        /// </summary>
        public ExcelResolver<Lumina.Excel.GeneratedSheets.Pet> PetData => new(this.DataID, this.dalamud);

        /// <summary>
        /// Gets the Trust data related to this buddy. It should only be used with battle buddies.
        /// </summary>
        public ExcelResolver<Lumina.Excel.GeneratedSheets.DawnGrowMember> TrustData => new(this.DataID, this.dalamud);

        private FFXIVClientStructs.FFXIV.Client.Game.UI.Buddy.BuddyMember* Struct => (FFXIVClientStructs.FFXIV.Client.Game.UI.Buddy.BuddyMember*)this.Address;
    }
}
