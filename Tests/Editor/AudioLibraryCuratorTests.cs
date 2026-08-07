using System;
using System.IO;
using NUnit.Framework;

namespace MooMoo.AudioLibraryCurator.Tests
{
    public sealed class AudioLibraryCuratorTests
    {
        string _temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"audio-curator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
        }

        [Test]
        public void VendorMetadataParsesSemicolonColumnsAndQuotedSeparators()
        {
            const string csv =
                "FileName;Time;Format;Channels;Library;Description;Originator\r\n" +
                "book.wav;0:03;96 24;2;Books;\"Close; weighty, dry\";344 Audio\r\n";

            var metadata = AudioLibraryScanner.ParseMetadata(csv);

            Assert.That(metadata.ContainsKey("book.wav"), Is.True);
            Assert.That(metadata["book.wav"].Duration, Is.EqualTo("0:03"));
            Assert.That(metadata["book.wav"].Description, Is.EqualTo("Close; weighty, dry"));
            Assert.That(metadata["book.wav"].Originator, Is.EqualTo("344 Audio"));
        }

        [Test]
        public void ScannerFindsAudioRecursivelyAndIgnoresDocumentation()
        {
            var sounds = Path.Combine(_temporaryDirectory, "Sound Effects");
            Directory.CreateDirectory(sounds);
            File.WriteAllBytes(Path.Combine(sounds, "page.wav"), Array.Empty<byte>());
            File.WriteAllText(Path.Combine(_temporaryDirectory, "EULA.pdf"), "not audio");

            var items = AudioLibraryScanner.Scan(new AudioLibraryDefinition
            {
                id = "library",
                name = "Test Library",
                rootPath = _temporaryDirectory,
            });

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].RelativePath, Is.EqualTo("Sound Effects/page.wav"));
        }

        [TestCase("Assets", true)]
        [TestCase("Assets/Audio/Curated", true)]
        [TestCase("Assets\\Audio\\Curated", true)]
        [TestCase("Packages/Audio", false)]
        [TestCase("Assets/../Packages", false)]
        [TestCase("/tmp/Audio", false)]
        public void ImportDestinationMustStayUnderAssets(string path, bool expected)
        {
            Assert.That(AudioPath.IsValidAssetDestination(path), Is.EqualTo(expected));
        }

        [TestCase("Sound Effects/book.wav", true)]
        [TestCase("book.wav", true)]
        [TestCase("../book.wav", false)]
        [TestCase("Sound Effects/../../book.wav", false)]
        [TestCase("/tmp/book.wav", false)]
        public void LibraryRelativePathsCannotEscapeTheImportRoot(string path, bool expected)
        {
            Assert.That(AudioPath.IsSafeRelativePath(path), Is.EqualTo(expected));
        }

        [Test]
        public void CollisionNeverOverwritesDifferentPurchasedAudio()
        {
            var candidate = Path.Combine(_temporaryDirectory, "book.wav");
            var source = Path.Combine(_temporaryDirectory, "source.wav");
            File.WriteAllText(candidate, "first recording");
            File.WriteAllText(source, "second recording");

            var allocated = AudioIngest.UniqueOrMatchingPath(candidate, AudioIngest.Sha256(source));

            Assert.That(allocated, Is.EqualTo(Path.Combine(_temporaryDirectory, "book_2.wav").Replace('\\', '/')));
            Assert.That(File.ReadAllText(candidate), Is.EqualTo("first recording"));
        }

        [Test]
        public void IdenticalAudioReusesExistingDestination()
        {
            var candidate = Path.Combine(_temporaryDirectory, "book.wav");
            File.WriteAllText(candidate, "same recording");

            var allocated = AudioIngest.UniqueOrMatchingPath(candidate, AudioIngest.Sha256(candidate));

            Assert.That(allocated, Is.EqualTo(candidate));
        }

        [Test]
        public void InstalledUnityVersionExposesAnEditorPreviewMethod()
        {
            Assert.That(AudioPreviewBridge.IsAvailable, Is.True,
                "Unity's internal audio preview signature changed; update AudioPreviewBridge before using the curator.");
        }
    }
}
