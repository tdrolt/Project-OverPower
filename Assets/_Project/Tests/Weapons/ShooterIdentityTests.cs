using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Net;
using Overpower.Weapons;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// Who fired a shot is the shooting body's owner on every client. The host sends every bot's fire RPC, so PhotonMessageInfo.Sender would
    /// credit the host with the bot's hits and let them fly through the host's teammates; for a human the owner and the sender are the same player.
    /// </summary>
    public class ShooterIdentityTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static MethodInfo ShooterOf => typeof(WeaponFiring).GetMethod("ShooterOf", Any);

        [Test]
        public void EveryClientBuildsTheShotForTheBodysOwner()
        {
            Assert.IsNotNull(ShooterOf, "WeaponFiring.ShooterOf is the one place a shot learns who fired it");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "RPC_FireWeapon", ShooterOf), "the fire RPC asks the body who fired");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(WeaponFiring), "ShooterOf",
                typeof(PhotonView).GetProperty(nameof(PhotonView.OwnerActorNr)).GetMethod), "the shooter is the body's owner");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(WeaponFiring), "ShooterOf",
                typeof(PhotonView).GetProperty(nameof(PhotonView.Owner)).GetMethod), "and so is the team it fires for");
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "ShooterOf",
                typeof(Teams).GetMethod(nameof(Teams.TryGetTeam), new[] { typeof(Photon.Realtime.Player), typeof(int).MakeByRefType() })));
        }

        [Test]
        public void TheLaserRefundRayPassesThroughTheSameFriendsAsTheRealBeam()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(WeaponFiring), "RefundHeatIfBeamConnects", ShooterOf));
        }

        [Test]
        public void NothingInTheTriggerReadsTheMessageSenderOrTheLocalPlayer()
        {
            const BindingFlags Declared = Any | BindingFlags.DeclaredOnly;
            FieldInfo sender = typeof(PhotonMessageInfo).GetField(nameof(PhotonMessageInfo.Sender));
            MethodBase[] methods = typeof(WeaponFiring).GetMethods(Declared).Cast<MethodBase>()
                .Concat(typeof(WeaponFiring).GetNestedTypes(Any).SelectMany(t => t.GetMethods(Declared)).Cast<MethodBase>()).ToArray();
            Assert.IsNotEmpty(methods);
            Assert.IsTrue(LoadsField(typeof(FireField).GetMethod(nameof(FireField.OnPhotonInstantiate)), sender),
                "the check sees a real read: the fire field still falls back to the sender");
            foreach (MethodBase method in methods)
                Assert.IsFalse(LoadsField(method, sender), method.Name + " reads info.Sender, which is the host for a bot's shot");

            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(WeaponFiring), typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetMethod),
                "PhotonNetwork.LocalPlayer is the host on the host, never a bot it fires for");
        }

        // A ldfld/ldflda of a field that may live in another assembly (Photon): each candidate token is resolved, as IlWiring.CallOffsets does for calls.
        private static bool LoadsField(MethodBase method, FieldInfo field)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x7B && il[i] != 0x7C) continue;
                int token = System.BitConverter.ToInt32(il, i + 1);
                int table = (token >> 24) & 0xFF;
                if (table != 0x04 && table != 0x0A) continue; // FieldDef, MemberRef
                try
                {
                    FieldInfo resolved = method.Module.ResolveField(token);
                    if (resolved != null && resolved.Module == field.Module && resolved.MetadataToken == field.MetadataToken) return true;
                }
                catch (System.Exception) { /* not a field token at this offset */ }
            }
            return false;
        }
    }
}
