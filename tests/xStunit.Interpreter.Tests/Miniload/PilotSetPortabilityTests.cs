using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using xStunit.Parser;

namespace xStunit.Interpreter.Tests
{
    // The Grade B oracle diffs verdicts from the same POUs run twice, once here
    // and once on a real TwinCAT runtime, so every POU in the pilot set has to
    // build on the target. An intrinsic xStunit dispatches but TwinCAT has no
    // counterpart for breaks that quietly: the fixture stays green here and
    // simply stops compiling there. If this goes red, a pilot POU has picked
    // up a call the target cannot build - move the case into an xStunit-only
    // suite of its own rather than adding it to the exclusion list below.
    public class PilotSetPortabilityTests
    {
        // Free-function intrinsics EvaluateCall dispatches that TwinCAT does not
        // have. ADR, SIZEOF, CONCAT, MEMCPY, MEMMOVE and MEMSET are all real
        // TwinCAT functions and deliberately stay off this list; it grows as the
        // interpreter gains more xStunit-only surface.
        private static readonly string[] NonPortableIntrinsics = { "AdvanceClock" };

        // The pilot set: the miniload fixtures are the artefact the Grade B
        // verdict diff is pointed at, so their POUs are the ones that must build
        // under real TwinCAT.
        private static readonly string[] PilotFixtureDirs =
        {
            TestFixtures.MiniloadConveyorFixtureDir(),
            TestFixtures.MiniloadPackMLFixtureDir(),
            TestFixtures.MiniloadSensorFixtureDir(),
        };

        // POUs that sit inside a pilot fixture directory but are declared
        // xStunit-only, and are therefore outside the pilot set by decision. A
        // POU earns a place here only when running on the target is not a
        // property it ever claimed.
        private static readonly string[] DeclaredXstunitOnlyPous = { "FB_DigitalInputTimingTests" };

        [Fact]
        public void PilotSetPous_CallNoIntrinsicTheTargetLacks()
        {
            var offenders = PilotSetPouFiles()
                .SelectMany(NonPortableCallsIn)
                .ToList();

            Assert.True(
                offenders.Count == 0,
                "pilot-set POUs call intrinsics TwinCAT does not have:\n" + string.Join("\n", offenders));
        }

        // Without this the guard would pass by scanning nothing at all - a
        // renamed or moved fixture directory would silently disarm it.
        [Fact]
        public void PilotSetPous_AreActuallyFound()
        {
            foreach (var dir in PilotFixtureDirs)
                Assert.NotEmpty(Directory.GetFiles(dir, "*.TcPOU", SearchOption.AllDirectories));

            Assert.NotEmpty(PilotSetPouFiles());
        }

        // The detector itself, pinned on text that is not in the tree: proves
        // the scan above is empty because the pilot POUs are clean, not because
        // it stopped recognizing a call.
        [Fact]
        public void NonPortableCalls_AreFoundInABodyThatMakesOne()
        {
            var body = "sensor.SimulatedInput := TRUE;\nAdvanceClock(T#20ms);";

            Assert.True(CallsIntrinsic(body, "AdvanceClock"));
        }

        // Prose about an intrinsic is not a call to it, so the POU comment that
        // explains why a suite avoids AdvanceClock cannot fail the guard.
        [Fact]
        public void NonPortableCalls_IgnoreCommentedOutAndDescribedCalls()
        {
            var body = "(* nothing here may call AdvanceClock(T#1ms) *)\n// AdvanceClock(T#1ms);\nsensor.StepCycles(1);";

            Assert.False(CallsIntrinsic(body, "AdvanceClock"));
        }

        // Keeps the exclusion list from rotting into a blanket waiver: a POU
        // listed as xStunit-only that no longer calls anything xStunit-only is
        // portable again and belongs back in the pilot set.
        [Fact]
        public void ExcludedPous_StillCallSomethingTheTargetLacks()
        {
            foreach (var name in DeclaredXstunitOnlyPous)
            {
                var file = PouFilesUnderPilotFixtures().Single(f => PouNameOf(f) == name);

                Assert.NotEmpty(NonPortableCallsIn(file));
            }
        }

        private static List<string> PilotSetPouFiles() =>
            PouFilesUnderPilotFixtures()
                .Where(f => !DeclaredXstunitOnlyPous.Contains(PouNameOf(f)))
                .ToList();

        private static List<string> PouFilesUnderPilotFixtures() =>
            PilotFixtureDirs
                .SelectMany(d => Directory.GetFiles(d, "*.TcPOU", SearchOption.AllDirectories))
                .OrderBy(f => f)
                .ToList();

        private static string PouNameOf(string pouFile) => Path.GetFileNameWithoutExtension(pouFile);

        private static List<string> NonPortableCallsIn(string pouFile)
        {
            var pou = TcPouParser.Parse(File.ReadAllText(pouFile));

            return (from body in BodiesOf(pou)
                    from intrinsic in NonPortableIntrinsics
                    where CallsIntrinsic(body.Value, intrinsic)
                    select $"{Path.GetFileName(pouFile)}: {pou.Name}.{body.Key} calls {intrinsic}")
                .ToList();
        }

        private static IEnumerable<KeyValuePair<string, string>> BodiesOf(PouAst pou)
        {
            yield return Body("<declaration>", pou.DeclarationText);
            yield return Body("<body>", pou.ImplementationText);

            foreach (var method in pou.Methods)
            {
                yield return Body(method.Name + ".<declaration>", method.DeclarationText);
                yield return Body(method.Name, method.ImplementationText);
            }

            foreach (var property in pou.Properties)
            {
                yield return Body(property.Name + ".<declaration>", property.DeclarationText);
                yield return Body(property.Name + ".Get", property.GetImplementationText);
                yield return Body(property.Name + ".Set", property.SetImplementationText);
            }
        }

        private static KeyValuePair<string, string> Body(string name, string text) =>
            new KeyValuePair<string, string>(name, text ?? string.Empty);

        // Matched case-insensitively because ST identifiers are, so no casing
        // trick gets a call past the guard.
        private static bool CallsIntrinsic(string body, string intrinsic) =>
            Regex.IsMatch(
                Lexer.StripComments(body),
                @"(?<![A-Za-z0-9_])" + Regex.Escape(intrinsic) + @"\s*\(",
                RegexOptions.IgnoreCase);
    }
}
