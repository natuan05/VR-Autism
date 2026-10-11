const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const root = path.resolve(__dirname, '../../../../../../..');
const definitionGuid = '0222d045d21b4349ba11d3b930df3151';
const definitionClass = 'Assembly-CSharp::VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphSceneDefinitionV2';
const installerGuid = '56d5eea2f17f4f388d9b8b4b8ed4b93e';
const installerClass = 'Assembly-CSharp::VRAutism.Gameplay.LessonGraphV2.Runtime.LessonGraphRunnerInstaller';

const scenes = [
  {
    path: 'Assets/Project/Scenes/Bathroom-V2.unity',
    componentId: '1130038379',
    graphGuid: 'ad32e9c811bab714783073ba2f9d7666',
    refs: {
      _runner: '1130038378',
      _bindings: '1130038377',
      _dialogueTransport: '1900500020',
      _remoteBridge: '1900500021',
      _timelinePlaybackController: '1347164648',
    },
  },
  {
    path: 'Assets/Project/Scenes/LearnToAsk-V2.unity',
    componentId: '1225870742',
    graphGuid: 'c334214253427c841b9dabb45326276a',
    refs: {
      _runner: '1225870744',
      _bindings: '1225870743',
      _dialogueTransport: '1225870739',
      _remoteBridge: '1225870738',
      _timelinePlaybackController: '405125986',
    },
  },
];

function gitText(file) {
  return execFileSync('git', ['show', `HEAD:${file}`], {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  }).replace(/\r\n/g, '\n');
}

function documents(text) {
  const normalized = text.replace(/\r\n/g, '\n');
  const result = new Map();
  for (const block of normalized.split(/(?=^--- !u!)/m)) {
    const header = block.match(/^--- !u!\d+ &(-?\d+)/);
    if (header) result.set(header[1], block);
  }
  return result;
}

function requireDocument(map, id, file) {
  const block = map.get(id);
  assert.ok(block, `${file}: missing Unity document ${id}`);
  return block;
}

function guidForMeta(metaPath) {
  const match = fs.readFileSync(metaPath, 'utf8').match(/^guid: ([0-9a-f]{32})$/m);
  assert.ok(match, `${metaPath}: missing Unity GUID`);
  return match[1];
}

function findGuidMeta(guid) {
  return walkFiles(path.join(root, 'Assets'), '.meta')
    .filter((file) => new RegExp(`^guid: ${guid}$`, 'm').test(fs.readFileSync(file, 'utf8')))
    .map((file) => path.relative(root, file).replace(/\\/g, '/'));
}

function walkFiles(dir, extension) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory()
      ? walkFiles(full, extension)
      : entry.name.endsWith(extension) ? [full] : [];
  });
}

function checkScene(scene) {
  const workingText = fs.readFileSync(path.join(root, scene.path), 'utf8').replace(/\r\n/g, '\n');
  const baseDocs = documents(gitText(scene.path));
  const workingDocs = documents(workingText);
  const oldDefinition = requireDocument(baseDocs, scene.componentId, scene.path);
  const definition = requireDocument(workingDocs, scene.componentId, scene.path);

  assert.match(definition, new RegExp(`^  m_Script: \\{fileID: 11500000, guid: ${definitionGuid}, type: 3\\}$`, 'm'));
  assert.match(definition, new RegExp(`^  m_EditorClassIdentifier: ${definitionClass.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'm'));
  assert.doesNotMatch(workingText, new RegExp(installerGuid));
  assert.doesNotMatch(workingText, new RegExp(installerClass.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  assert.match(definition, new RegExp(`^  _lessonGraph: \\{fileID: 11400000, guid: ${scene.graphGuid}, type: 2\\}$`, 'm'));
  const graphMeta = findGuidMeta(scene.graphGuid);
  assert.equal(graphMeta.length, 1, `${scene.path}: graph GUID must resolve uniquely`);
  assert.ok(graphMeta[0].endsWith('.asset.meta'), `${scene.path}: graph GUID must resolve to an asset`);
  assert.doesNotMatch(definition, /^  _startOnStart:/m);

  for (const [field, refId] of Object.entries(scene.refs)) {
    assert.match(definition, new RegExp(`^  ${field}: \\{fileID: ${refId}\\}$`, 'm'), `${scene.path}: ${field} changed`);
    requireDocument(workingDocs, refId, scene.path);
  }
  assert.match(definition, /^  _variableStore: \{fileID: 0\}$/m);

  const normalizedOriginal = oldDefinition
    .replace(installerGuid, definitionGuid)
    .replace(installerClass, definitionClass)
    .replace(/^  _startOnStart: 1\n/m, '');
  assert.equal(definition, normalizedOriginal, `${scene.path}: definition block has unexpected serialization changes`);

  assert.equal(workingDocs.size, baseDocs.size, `${scene.path}: Unity document count changed`);
  for (const [id, block] of baseDocs) {
    if (id === scene.componentId) continue;
    assert.equal(workingDocs.get(id), block, `${scene.path}: unrelated Unity document ${id} changed`);
  }

  const timelineComponent = [...workingDocs.values()].find((block) =>
    block.includes('VRAutism.Gameplay.LessonGraphV2.Runtime.TimelinePlaybackController'));
  assert.ok(timelineComponent, `${scene.path}: missing TimelinePlaybackController`);
  const directorId = timelineComponent.match(/^  _director: \{fileID: (\d+)\}$/m)?.[1];
  assert.ok(directorId, `${scene.path}: TimelinePlaybackController has no director reference`);
  const director = requireDocument(workingDocs, directorId, scene.path);
  assert.match(director, /^PlayableDirector:/m);
  assert.doesNotMatch(director, /^  m_PlayOnAwake: 1$/m, `${scene.path}: lesson-owned director auto-plays`);

  console.log(`PASS ${scene.path}: definition wiring, unchanged component documents, and director state`);
}

for (const scene of scenes) checkScene(scene);

const definitionMeta = 'Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/LessonGraphSceneDefinitionV2.cs.meta';
assert.equal(guidForMeta(path.join(root, definitionMeta)), definitionGuid, 'definition script GUID mismatch');
assert.deepEqual(findGuidMeta(definitionGuid), [definitionMeta], 'definition script GUID does not resolve uniquely');

const forbiddenNames = /Bathroom(?:-V2)?|LearnToAsk(?:-V2)?/;
const runtimeDir = path.join(root, 'Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime');
for (const file of walkFiles(runtimeDir, '.cs')) {
  const source = fs.readFileSync(file, 'utf8');
  assert.doesNotMatch(source, forbiddenNames, `${path.relative(root, file)} contains a lesson/scene name`);
}

for (const untouched of [
  'Assets/Project/Scenes/HelloFriend.unity',
  'Assets/Project/Scenes/HelloFriend-V2.unity',
  'Assets/Project/Scenes/GameMenu.unity',
]) {
  assert.equal(
    fs.readFileSync(path.join(root, untouched), 'utf8').replace(/\r\n/g, '\n'),
    gitText(untouched),
    `${untouched} changed`,
  );
  console.log(`PASS ${untouched}: unchanged`);
}

console.log('Production lesson scene migration audit passed.');
