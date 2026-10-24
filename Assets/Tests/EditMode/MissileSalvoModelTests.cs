using NUnit.Framework;
using OverTheHorizon.Combat.Missiles;
using Unity.Entities;

namespace OverTheHorizon.Tests
{
    public sealed class MissileSalvoModelTests
    {
        [Test]
        public void LogicalKeysRemainStableAndDistinguishSalvosAndMembers()
        {
            var first = new SalvoLogicalKey(100, 3);
            var same = new SalvoLogicalKey(100, 3);
            var next = new SalvoLogicalKey(100, 4);
            var firstMember = new MemberLogicalKey(first, 0);
            var secondMember = new MemberLogicalKey(first, 1);

            Assert.AreEqual(first, same);
            Assert.AreNotEqual(first, next);
            Assert.AreNotEqual(firstMember, secondMember);
            Assert.AreNotEqual(firstMember, new MemberLogicalKey(next, 0));
            Assert.IsTrue(firstMember.IsValid);
        }

        [Test]
        public void SeedDerivationIsStableAndIndependentOfMemberCreationOrder()
        {
            var salvo = new SalvoLogicalKey(100, 3);
            var memberA = new MemberLogicalKey(salvo, 2);
            var memberB = new MemberLogicalKey(salvo, 9);
            ulong salvoSeed = MissileSeedDerivation.DeriveSalvoSeed(12345, salvo);

            ulong aFirst = MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberA, MissileSeedDomain.MemberVariation);
            ulong bFirst = MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberB, MissileSeedDomain.MemberVariation);
            ulong bSecond = MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberB, MissileSeedDomain.MemberVariation);
            ulong aSecond = MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberA, MissileSeedDomain.MemberVariation);

            Assert.AreEqual(aFirst, aSecond);
            Assert.AreEqual(bFirst, bSecond);
            Assert.AreNotEqual(aFirst, bFirst);
            Assert.AreNotEqual(aFirst, MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberA, MissileSeedDomain.Scheduling));
            Assert.AreNotEqual(salvoSeed, MissileSeedDerivation.DeriveSalvoSeed(12346, salvo));
        }

        [Test]
        public void MemberReferenceRejectsInactiveDestroyedAndWrongGenerationSalvos()
        {
            using (var world = new World("Missile Salvo membership"))
            {
                var manager = world.EntityManager;
                var profile = manager.CreateEntity();
                var salvo = manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoProfileReference));
                var key = new SalvoLogicalKey(100, 3);
                manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 7 });
                manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profile });

                var member = manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference), typeof(MissileMemberProfileReference));
                manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, 0), SessionGeneration = 7 });
                manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 7, Active = 1 });
                manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profile });

                Assert.IsTrue(MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, 7, out var resolved));
                Assert.AreEqual(salvo, resolved);
                Assert.IsTrue(MissileSalvoModelValidation.HasSharedProfile(manager, member, salvo));
                Assert.IsFalse(MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, 8, out _));

                var reference = manager.GetComponentData<MissileMemberSalvoReference>(member);
                reference.Active = 0;
                manager.SetComponentData(member, reference);
                Assert.IsFalse(MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, 7, out _));

                reference.Active = 1;
                manager.SetComponentData(member, reference);
                manager.DestroyEntity(salvo);
                Assert.IsFalse(MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, 7, out _));
            }
        }
    }
}
