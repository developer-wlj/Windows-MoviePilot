"use strict";

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports.canAccessFile = canAccessFile;
exports.copyFileAndMakeWritable = copyFileAndMakeWritable;
exports.existsAsync = void 0;
exports.mkdirIfNeeded = mkdirIfNeeded;
exports.removeFolders = removeFolders;
exports.sanitizeForFilePath = sanitizeForFilePath;
exports.writeFileSyncAtomic = writeFileSyncAtomic;
var _fs = _interopRequireDefault(require("fs"));
var _path = _interopRequireDefault(require("path"));
var _utilsBundle = require("../utilsBundle");
var _crypto = require("./crypto");
function _interopRequireDefault(obj) { return obj && obj.__esModule ? obj : { default: obj }; }
/**
 * Copyright (c) Microsoft Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

const existsAsync = path => new Promise(resolve => _fs.default.stat(path, err => resolve(!err)));
exports.existsAsync = existsAsync;
async function mkdirIfNeeded(filePath) {
  // This will harmlessly throw on windows if the dirname is the root directory.
  await _fs.default.promises.mkdir(_path.default.dirname(filePath), {
    recursive: true
  }).catch(() => {});
}
async function removeFolders(dirs) {
  return await Promise.all(dirs.map(dir => {
    return new Promise(fulfill => {
      (0, _utilsBundle.rimraf)(dir, {
        maxRetries: 10
      }).then(() => fulfill(undefined)).catch(e => fulfill(e));
    });
  }));
}
function canAccessFile(file) {
  if (!file) return false;
  try {
    _fs.default.accessSync(file);
    return true;
  } catch (e) {
    return false;
  }
}
async function copyFileAndMakeWritable(from, to) {
  await _fs.default.promises.copyFile(from, to);
  await _fs.default.promises.chmod(to, 0o664);
}
function sanitizeForFilePath(s) {
  return s.replace(/[\x00-\x2C\x2E-\x2F\x3A-\x40\x5B-\x60\x7B-\x7F]+/g, '-');
}
function writeFileSyncAtomic(aPath, data, options) {
  const dirName = _path.default.dirname(aPath);
  const fileName = _path.default.basename(aPath);
  const tmpPath = _path.default.join(dirName, fileName + '-' + (0, _crypto.createGuid)());
  _fs.default.writeFileSync(tmpPath, data, options);
  _fs.default.renameSync(tmpPath, aPath);
}