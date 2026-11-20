/*
  ================================================================================================
  RMS MODEL SCANNER - THE EXPLORER
  ================================================================================================

  Reads a model's payload in the reader's browser and draws the instance tree it contains.

  Two formats, because Roblox publishes two. A binary model (`.rbxm`) is a chunked container: a class
  table, an instance table, property chunks keyed by class, and a chunk that names each instance's
  parent. An XML model (`.rbxmx`) is the same information written as elements. Both arrive through the
  site's download handler, which gives the browser the same bytes a visitor could have saved.

  The binary reader is the part worth being careful about. A chunk is either stored or LZ4-compressed,
  and property values are a typed stream with no per-value length prefix, so a single type read at the
  wrong width makes everything after it nonsense. Everything here therefore either reads a type it
  knows at a width it knows, or stops and says where. A half-read model still draws the tree: the
  instance table and the parent chunk come first, and they are enough to show the shape.
*/

/*
  The whole fourteen-byte signature, not the first ten of it.

  `<roblox!` is eight bytes and the magic that follows is six: 0x89 0xff 0x0d 0x0a 0x1a 0x0a. Stopping
  after 0xff leaves the reader's cursor two bytes short, and the next two bytes of the file are 0x0d and
  0x0a -- a carriage return and a line feed that a u16 read turns into 2573. That is not a version any
  writer produced; it is the tail of the signature being read as one. The signature is therefore kept
  whole and the cursor starts after all of it.
*/
const BINARY_MAGIC = [60, 114, 111, 98, 108, 111, 120, 33, 137, 255, 13, 10, 26, 10];

/* Property type identifiers, as the binary format numbers them. */
const TYPE_STRING = 0x01;
const TYPE_BOOL = 0x02;
const TYPE_INT32 = 0x03;
const TYPE_FLOAT32 = 0x04;
const TYPE_FLOAT64 = 0x05;
const TYPE_UDIM = 0x06;
const TYPE_UDIM2 = 0x07;
const TYPE_RAY = 0x08;
const TYPE_FACES = 0x09;
const TYPE_AXES = 0x0a;
const TYPE_BRICKCOLOR = 0x0b;
const TYPE_COLOR3 = 0x0c;
const TYPE_VECTOR2 = 0x0d;
const TYPE_VECTOR3 = 0x0e;
const TYPE_VECTOR2INT16 = 0x0f;
const TYPE_CFRAME = 0x10;
const TYPE_QUATERNION = 0x11;
const TYPE_ENUM = 0x12;
const TYPE_REF = 0x13;
const TYPE_VECTOR3INT16 = 0x14;
const TYPE_NUMBERSEQUENCE = 0x15;
const TYPE_COLORSEQUENCE = 0x16;
const TYPE_NUMBERRANGE = 0x17;
const TYPE_RECT = 0x18;
const TYPE_PHYSICALPROPERTIES = 0x19;
const TYPE_COLOR3UINT8 = 0x1a;
const TYPE_INT64 = 0x1b;
const TYPE_SHAREDSTRING = 0x1c;
const TYPE_BYTECODE = 0x1d;
const TYPE_OPTIONALCFRAME = 0x1e;
const TYPE_UNIQUEID = 0x1f;
const TYPE_FONT = 0x20;
const TYPE_SECURITYCAPABILITIES = 0x21;
const TYPE_CONTENT = 0x22;
const TYPE_NAMES = {
    [TYPE_STRING]: 'String', [TYPE_BOOL]: 'Bool', [TYPE_INT32]: 'Int32',
    [TYPE_FLOAT32]: 'Float32', [TYPE_FLOAT64]: 'Float64', [TYPE_UDIM]: 'UDim',
    [TYPE_UDIM2]: 'UDim2', [TYPE_RAY]: 'Ray', [TYPE_FACES]: 'Faces', [TYPE_AXES]: 'Axes',
    [TYPE_BRICKCOLOR]: 'BrickColor', [TYPE_COLOR3]: 'Color3', [TYPE_VECTOR2]: 'Vector2',
    [TYPE_VECTOR3]: 'Vector3', [TYPE_VECTOR2INT16]: 'Vector2int16', [TYPE_CFRAME]: 'CFrame',
    [TYPE_QUATERNION]: 'Quaternion', [TYPE_ENUM]: 'Enum', [TYPE_REF]: 'Ref',
    [TYPE_VECTOR3INT16]: 'Vector3int16', [TYPE_NUMBERSEQUENCE]: 'NumberSequence',
    [TYPE_COLORSEQUENCE]: 'ColorSequence', [TYPE_NUMBERRANGE]: 'NumberRange', [TYPE_RECT]: 'Rect',
    [TYPE_PHYSICALPROPERTIES]: 'PhysicalProperties', [TYPE_COLOR3UINT8]: 'Color3uint8',
    [TYPE_INT64]: 'Int64', [TYPE_SHAREDSTRING]: 'SharedString', [TYPE_BYTECODE]: 'Bytecode',
    [TYPE_OPTIONALCFRAME]: 'OptionalCoordinateFrame', [TYPE_UNIQUEID]: 'UniqueId',
    [TYPE_FONT]: 'Font', [TYPE_SECURITYCAPABILITIES]: 'SecurityCapabilities',
    [TYPE_CONTENT]: 'Content'
};

/*
  The capability bits a SecurityCapabilities mask carries, with the names Roblox gives them. Spelled in
  BigInt because the top of the mask is above the range an ordinary number holds exactly, and a mask that
  loses its high bits is a mask that reads as a different set of permissions than the file declared.
*/
const CAPABILITY_NAMES = [
    [1n << 8n, 'RunClientScript'], [1n << 9n, 'RunServerScript'],
    [1n << 11n, 'AccessOutsideWrite'], [1n << 16n, 'AssetRequire'],
    [1n << 17n, 'LoadString'], [1n << 18n, 'ScriptGlobals'],
    [1n << 19n, 'CreateInstances'], [1n << 20n, 'Basic'],
    [1n << 21n, 'Audio'], [1n << 22n, 'DataStore'], [1n << 23n, 'Network'],
    [1n << 24n, 'Physics'], [1n << 25n, 'UI'], [1n << 26n, 'CSG'],
    [1n << 27n, 'Chat'], [1n << 28n, 'Animation'], [1n << 29n, 'Avatar'],
    [1n << 30n, 'Input'], [1n << 31n, 'Environment'], [1n << 32n, 'RemoteEvent'],
    [1n << 33n, 'LegacySound'], [1n << 34n, 'Players'], [1n << 35n, 'CapabilityControl']
];

/* The classes whose whole reason for existing is the source they carry. */
const SOURCE_CLASSES = new Set(['Script', 'LocalScript', 'ModuleScript']);

/* The longest property text written into a row before it is cut off. */
const PROPERTY_TEXT_LIMIT = 240;

/* The longest script source shown in full. */
const SOURCE_LIMIT = 131072;

const decoder = new TextDecoder('utf-8');

/**
 * A little-endian cursor over a byte array.
 */
class Cursor {
    /**
     * @param {Uint8Array} bytes the bytes to read.
     */
    constructor(bytes) {
        this.bytes = bytes;
        this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        this.at = 0;
    }

    /** @returns {number} the bytes not yet read. */
    get left() {
        return this.bytes.length - this.at;
    }

    /** @returns {number} the next unsigned byte. */
    u8() {
        return this.bytes[this.at++];
    }

    /** @returns {number} the next signed byte. */
    i8() {
        return this.view.getInt8(this.at++);
    }

    /** @returns {number} the next unsigned 16-bit integer. */
    u16() {
        const value = this.view.getUint16(this.at, true);
        this.at += 2;
        return value;
    }

    /** @returns {number} the next signed 16-bit integer. */
    i16() {
        const value = this.view.getInt16(this.at, true);
        this.at += 2;
        return value;
    }

    /** @returns {number} the next unsigned 32-bit integer. */
    u32() {
        const value = this.view.getUint32(this.at, true);
        this.at += 4;
        return value;
    }

    /** @returns {number} the next signed 32-bit integer. */
    i32() {
        const value = this.view.getInt32(this.at, true);
        this.at += 4;
        return value;
    }

    /** @returns {number} the next 32-bit float. */
    f32() {
        const value = this.view.getFloat32(this.at, true);
        this.at += 4;
        return value;
    }

    /** @returns {number} the next 64-bit float. */
    f64() {
        const value = this.view.getFloat64(this.at, true);
        this.at += 8;
        return value;
    }

    /** @returns {number} the next 64-bit integer, as a number. */
    i64() {
        const value = this.view.getBigInt64(this.at, true);
        this.at += 8;
        return Number(value);
    }

    /**
     * @param {number} count how many bytes to skip.
     * @returns {void}
     */
    skip(count) {
        this.at += count;
    }

    /**
     * @param {number} count how many bytes to take.
     * @returns {Uint8Array} the bytes.
     */
    take(count) {
        const slice = this.bytes.subarray(this.at, this.at + count);
        this.at += count;
        return slice;
    }

    /** @returns {string} a length-prefixed UTF-8 string. */
    str() {
        const length = this.u32();
        return decoder.decode(this.take(length));
    }
}

/**
 * Decompresses one LZ4 block.
 *
 * @param {Uint8Array} source the compressed bytes.
 * @param {number} size the decompressed length, which the chunk header states.
 * @returns {Uint8Array} the decompressed bytes.
 */
function decompressLz4(source, size) {
    const target = new Uint8Array(size);
    let from = 0;
    let to = 0;

    while (from < source.length) {
        const token = source[from++];

        let literals = token >> 4;

        if (literals === 15) {
            let extra;
            do {
                extra = source[from++];
                literals += extra;
            } while (extra === 255);
        }

        for (let index = 0; index < literals; index++) target[to++] = source[from++];

        if (from >= source.length) break;

        const offset = source[from++] | (source[from++] << 8);

        let match = token & 15;

        if (match === 15) {
            let extra;
            do {
                extra = source[from++];
                match += extra;
            } while (extra === 255);
        }

        match += 4;

        let read = to - offset;

        for (let index = 0; index < match; index++) target[to++] = target[read++];
    }

    return target;
}

/**
 * Reads four ASCII bytes as a chunk name.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {string} the name, with the terminator of END removed.
 */
function chunkName(bytes) {
    let name = '';
    for (let index = 0; index < 4; index++) name += String.fromCharCode(bytes[index]);
    return name.replace(/\0/g, '');
}

/**
 * Formats a number for display.
 *
 * @param {number} value the number.
 * @returns {string} the text.
 */
function num(value) {
    if (!isFinite(value)) return String(value);
    if (Number.isInteger(value)) return String(value);
    const rounded = Math.round(value * 10000) / 10000;
    return String(rounded);
}

/**
 * Formats three floats as a vector.
 *
 * @param {Cursor} cursor the cursor.
 * @returns {string} the text.
 */
function vector(cursor) {
    return '(' + num(cursor.f32()) + ', ' + num(cursor.f32()) + ', ' + num(cursor.f32()) + ')';
}

/*
  HOW A CHUNK STORES A VALUE

  Two things about the binary format are easy to get wrong and both make every value after the first
  one wrong.

  The first is the byte order and the encoding. Int32 is big-endian and zigzag-transformed, so a
  negative number is stored as twice its magnitude minus one. Float32 is not IEEE-754 either: the sign
  bit is the last bit of the four bytes rather than the first, so the bytes have to be rotated before
  they mean anything. Float64, by contrast, is ordinary little-endian. Referents are Int32s that are
  also accumulated: each value is the delta from the one before it.

  The second is that a property chunk holds a value for every instance of its class, and those values
  are stored by column rather than by row. Three parts with a position each are written as all three
  X values, then all three Y values, then all three Z values. Reading them one part at a time works
  for a class with a single instance -- which is what made this look right -- and is nonsense for a
  class with two.
*/

/**
 * Reads a column-major array of fixed-width values.
 *
 * @param {Cursor} cursor the chunk.
 * @param {number} count how many values the array holds.
 * @param {number} width how many bytes one value occupies.
 * @returns {Uint8Array[]} one row of bytes per value.
 * @throws {Error} when the chunk ends before the array does.
 */
function columns(cursor, count, width) {
    if (cursor.left < count * width) throw new Error('a value ends before the chunk does');

    const flat = cursor.take(count * width);
    const rows = new Array(count);

    for (let row = 0; row < count; row++) {
        const bytes = new Uint8Array(width);

        for (let column = 0; column < width; column++) bytes[column] = flat[column * count + row];

        rows[row] = bytes;
    }

    return rows;
}

/**
 * Reads eight bytes as a big-endian unsigned 64-bit integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {BigInt} the value.
 * @remarks
 * Spelled in two halves because a double cannot hold the top of a 64-bit mask exactly, and the masks this
 * reads are permissions: half of one is a different set of permissions.
 */
function u64be(bytes) {
    return (BigInt(u32be(bytes)) << 32n) | BigInt(u32be(bytes.subarray(4)));
}

/**
 * Reads four bytes as a big-endian unsigned integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {number} the value.
 */
function u32be(bytes) {
    return ((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]) >>> 0;
}

/**
 * Reads four bytes as a big-endian zigzag-transformed integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {number} the value.
 */
function i32be(bytes) {
    const raw = u32be(bytes);
    return (raw >>> 1) ^ -(raw & 1);
}

/**
 * Reads four bytes as a Roblox float.
 *
 * @param {Uint8Array} bytes the bytes, big-endian, with the sign bit last.
 * @returns {number} the value.
 */
function f32be(bytes) {
    const raw = u32be(bytes);
    floatBytes.setUint32(0, ((raw >>> 1) | ((raw & 1) << 31)) >>> 0, false);
    return floatBytes.getFloat32(0, false);
}

const floatBytes = new DataView(new ArrayBuffer(4));
const doubleBytes = new DataView(new ArrayBuffer(8));

/**
 * Reads eight bytes as a little-endian double.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {number} the value.
 */
function f64le(bytes) {
    for (let index = 0; index < 8; index++) doubleBytes.setUint8(index, bytes[index]);

    return doubleBytes.getFloat64(0, true);
}

/**
 * Reads eight bytes as a big-endian zigzag-transformed integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @returns {number} the value.
 */
function i64be(bytes) {
    // Arithmetic rather than a 64-bit integer type: the identifiers this carries are well inside the
    // range a double represents exactly, and the bundle is written for browsers that are not all
    // guaranteed a BigInt.
    let raw = 0;

    for (let index = 0; index < 8; index++) raw = raw * 256 + bytes[index];

    return raw % 2 === 0 ? raw / 2 : -(raw + 1) / 2;
}

/**
 * Reads two bytes as a little-endian signed integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @param {number} at where the value starts.
 * @returns {number} the value.
 */
function i16le(bytes, at) {
    const value = bytes[at] | (bytes[at + 1] << 8);
    return value & 0x8000 ? value - 0x10000 : value;
}

/**
 * Reads four bytes as a little-endian unsigned integer.
 *
 * @param {Uint8Array} bytes the bytes.
 * @param {number} at where the value starts.
 * @returns {number} the value.
 */
function u32le(bytes, at) {
    return (bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)) >>> 0;
}

/**
 * Reads an array of referents.
 *
 * @param {Cursor} cursor the chunk.
 * @param {number} count how many referents the array holds.
 * @returns {number[]} the referents, after accumulation.
 */
function readReferents(cursor, count) {
    const referents = [];
    let total = 0;

    for (const row of columns(cursor, count, 4)) {
        total += i32be(row);
        referents.push(total);
    }

    return referents;
}

/**
 * Reads the values one property chunk carries.
 *
 * @param {Cursor} cursor the chunk, positioned at the first value.
 * @param {number} type the property's type identifier.
 * @param {number} count how many instances of the class the values belong to.
 * @param {string[]} sharedStrings the shared-string table, indexed by the value's own index.
 * @returns {Object[]} one value per instance, in instance order.
 * @throws {Error} when the type is not one this reader knows.
 */
function readValues(cursor, type, count, sharedStrings) {
    switch (type) {
        case TYPE_STRING:
        case TYPE_BYTECODE: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const raw = cursor.str();
                values.push({ text: raw, raw: raw, kind: 'string' });
            }

            return values;
        }

        case TYPE_BOOL: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const raw = cursor.u8() !== 0;
                values.push({ text: raw ? 'true' : 'false', raw: raw });
            }

            return values;
        }

        case TYPE_INT32:
            return columns(cursor, count, 4).map((row) => {
                const raw = i32be(row);
                return { text: String(raw), raw: raw };
            });

        case TYPE_FLOAT32:
            return columns(cursor, count, 4).map((row) => {
                const raw = f32be(row);
                return { text: num(raw), raw: raw };
            });

        case TYPE_FLOAT64: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const raw = f64le(cursor.take(8));
                values.push({ text: num(raw), raw: raw });
            }

            return values;
        }

        case TYPE_UDIM: {
            const scale = columns(cursor, count, 4);
            const offset = columns(cursor, count, 4);

            return scale.map((row, index) => {
                const first = f32be(row);
                const second = i32be(offset[index]);
                return { text: num(first) + ', ' + second };
            });
        }

        case TYPE_UDIM2: {
            const xScale = columns(cursor, count, 4);
            const yScale = columns(cursor, count, 4);
            const xOffset = columns(cursor, count, 4);
            const yOffset = columns(cursor, count, 4);

            // The four components are stored X.Scale, Y.Scale, X.Offset, Y.Offset -- not the two axes
            // in turn, which is the order the value reads in once it is written out.
            return xScale.map((row, index) => ({
                text: '{' + num(f32be(row)) + ', ' + i32be(xOffset[index]) + '}, {'
                    + num(f32be(yScale[index])) + ', ' + i32be(yOffset[index]) + '}'
            }));
        }

        case TYPE_RAY: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const origin = vector(cursor);
                const direction = vector(cursor);
                values.push({ text: 'origin ' + origin + ', direction ' + direction });
            }

            return values;
        }

        case TYPE_FACES: {
            // Low six bits, in the order the format lists them: front, bottom, left, back, top, right.
            const names = ['front', 'bottom', 'left', 'back', 'top', 'right'];
            const values = [];

            for (let index = 0; index < count; index++) {
                const mask = cursor.u8();
                const set = names.filter((name, bit) => (mask & (1 << bit)) !== 0);
                values.push({ text: set.length === 0 ? 'none' : set.join(', ') });
            }

            return values;
        }

        case TYPE_AXES: {
            const names = ['x', 'y', 'z'];
            const values = [];

            for (let index = 0; index < count; index++) {
                const mask = cursor.u8();
                const set = names.filter((name, bit) => (mask & (1 << bit)) !== 0);
                values.push({ text: set.length === 0 ? 'none' : set.join(', ') });
            }

            return values;
        }

        case TYPE_BRICKCOLOR:
            return columns(cursor, count, 4).map((row) => {
                const raw = u32be(row);
                return { text: 'BrickColor ' + raw, raw: raw };
            });

        case TYPE_COLOR3: {
            const red = columns(cursor, count, 4);
            const green = columns(cursor, count, 4);
            const blue = columns(cursor, count, 4);

            return red.map((row, index) => ({
                text: rgbText(f32be(row), f32be(green[index]), f32be(blue[index]))
            }));
        }

        case TYPE_VECTOR2: {
            const x = columns(cursor, count, 4);
            const y = columns(cursor, count, 4);

            return x.map((row, index) => ({ text: '(' + num(f32be(row)) + ', ' + num(f32be(y[index])) + ')' }));
        }

        case TYPE_VECTOR3:
            return readVectors(cursor, count, 3).map((parts) => ({ text: '(' + parts.join(', ') + ')' }));

        case TYPE_VECTOR2INT16: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const bytes = cursor.take(4);
                values.push({ text: '(' + i16le(bytes, 0) + ', ' + i16le(bytes, 2) + ')' });
            }

            return values;
        }

        case TYPE_CFRAME: {
            /*
              Every value starts with an identifier. Zero means the rotation follows as nine floats; any
              other value is one of two dozen canned rotations and carries no bytes at all. The positions
              come after all of that, as one array -- which is why the rotations are stepped over here
              rather than read.
            */
            for (let index = 0; index < count; index++) {
                if (cursor.u8() === 0) cursor.skip(36);
            }

            return readVectors(cursor, count, 3).map((parts) => ({ text: 'position (' + parts.join(', ') + ')' }));
        }

        case TYPE_QUATERNION: {
            const parts = [];

            for (let component = 0; component < 4; component++) parts.push(columns(cursor, count, 4));

            return parts[0].map((row, index) => ({
                text: '(' + parts.map((part) => num(f32be(part[index]))).join(', ') + ')'
            }));
        }

        case TYPE_ENUM:
            return columns(cursor, count, 4).map((row) => {
                const raw = u32be(row);
                return { text: String(raw), raw: raw };
            });

        case TYPE_REF: {
            const referents = readReferents(cursor, count);

            return referents.map((raw) => ({
                text: raw < 0 ? 'none' : 'instance ' + raw,
                raw: raw
            }));
        }

        case TYPE_VECTOR3INT16: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const bytes = cursor.take(6);
                values.push({
                    text: '(' + i16le(bytes, 0) + ', ' + i16le(bytes, 2) + ', ' + i16le(bytes, 4) + ')'
                });
            }

            return values;
        }

        case TYPE_NUMBERSEQUENCE: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const points = cursor.u32();
                const parts = [];

                for (let point = 0; point < points; point++) {
                    const time = f32be(cursor.take(4));
                    const value = f32be(cursor.take(4));
                    const envelope = f32be(cursor.take(4));
                    parts.push(num(time) + ' -> ' + num(value)
                        + (envelope === 0 ? '' : ' (+/- ' + num(envelope) + ')'));
                }

                values.push({ text: parts.join('; ') });
            }

            return values;
        }

        case TYPE_COLORSEQUENCE: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const points = cursor.u32();
                const parts = [];

                // Time, red, green and blue, then a fifth float the format writes and does not use.
                for (let point = 0; point < points; point++) {
                    const time = f32be(cursor.take(4));
                    const red = f32be(cursor.take(4));
                    const green = f32be(cursor.take(4));
                    const blue = f32be(cursor.take(4));
                    cursor.skip(4);
                    parts.push(num(time) + ' ' + rgbText(red, green, blue));
                }

                values.push({ text: parts.join('; ') });
            }

            return values;
        }

        case TYPE_NUMBERRANGE: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const low = f32be(cursor.take(4));
                const high = f32be(cursor.take(4));
                values.push({ text: num(low) + ' to ' + num(high) });
            }

            return values;
        }

        case TYPE_RECT: {
            const parts = [];

            for (let component = 0; component < 4; component++) parts.push(columns(cursor, count, 4));

            return parts[0].map((row, index) => ({
                text: '(' + parts.map((part) => num(f32be(part[index]))).join(', ') + ')'
            }));
        }

        case TYPE_PHYSICALPROPERTIES: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const flags = cursor.u8();

                // Bit zero says custom values follow; bit one says the last of them is present.
                if ((flags & 1) === 0) {
                    values.push({ text: 'default' });
                    continue;
                }

                const density = f32be(cursor.take(4));
                const friction = f32be(cursor.take(4));
                const elasticity = f32be(cursor.take(4));
                cursor.skip(8);

                if ((flags & 2) !== 0) cursor.skip(4);

                values.push({
                    text: 'density ' + num(density) + ', friction ' + num(friction)
                        + ', elasticity ' + num(elasticity)
                });
            }

            return values;
        }

        case TYPE_COLOR3UINT8: {
            const red = columns(cursor, count, 1);
            const green = columns(cursor, count, 1);
            const blue = columns(cursor, count, 1);

            return red.map((row, index) => ({
                text: rgbText(row[0] / 255, green[index][0] / 255, blue[index][0] / 255)
            }));
        }

        case TYPE_INT64:
            return columns(cursor, count, 8).map((row) => {
                const raw = i64be(row);
                return { text: String(raw), raw: raw };
            });

        case TYPE_SHAREDSTRING:
            return columns(cursor, count, 4).map((row) => {
                const index = u32be(row);
                const text = index < sharedStrings.length ? sharedStrings[index] : 'shared string ' + index;
                return { text: text, raw: text, kind: 'string' };
            });

        case TYPE_UNIQUEID:
            return columns(cursor, count, 16).map((row) => {
                const raw = u32le(row, 0);
                return { text: 'id ' + raw, raw: raw };
            });

        case TYPE_FONT: {
            const values = [];

            for (let index = 0; index < count; index++) {
                const family = cursor.str();
                cursor.skip(2);
                cursor.skip(1);
                cursor.str();
                values.push({ text: family, raw: family, kind: 'string' });
            }

            return values;
        }

        case TYPE_OPTIONALCFRAME: {
            /*
              An optional value is written as the value itself, then a mask saying which of them are present.
              The pair of type identifiers around it are part of the encoding rather than decoration: both are
              tested, because a reader that assumed the inner type and was wrong would take the rest of the
              chunk apart at the wrong offset.
            */
            if (cursor.u8() !== TYPE_CFRAME) throw new Error('an optional value is not a CFrame');

            const frames = readValues(cursor, TYPE_CFRAME, count, sharedStrings);

            if (cursor.u8() !== TYPE_BOOL) throw new Error('an optional mask is not a Bool');

            return frames.map((frame) => {
                const present = cursor.u8() !== 0;
                return present ? frame : { text: '(none)' };
            });
        }

        case TYPE_SECURITYCAPABILITIES:
            return columns(cursor, count, 8).map((row) => {
                const raw = u64be(row);
                const set = CAPABILITY_NAMES.filter((entry) => (raw & entry[0]) !== 0n)
                    .map((entry) => entry[1]);

                return {
                    text: '0x' + raw.toString(16).padStart(16, '0')
                        + (set.length === 0 ? '' : ' (' + set.join(', ') + ')'),
                    raw: raw
                };
            });

        case TYPE_CONTENT: {
            /*
              A content value is one of three things: nothing, a URI, or a reference to an object in the file.
              All the URIs are written once in a table, then all the object references, and the per-value list
              of source types says which table a given value reads from. The two reference tables are stored
              as deltas, so each entry is added to the one before it.
            */
            const sourceTypes = columns(cursor, count, 4).map((row) => i32be(row));

            const uriCount = cursor.u32();
            const uris = [];

            for (let index = 0; index < uriCount; index++) uris.push(cursor.str());

            const objects = readReferents(cursor, cursor.u32());

            // References to objects outside this file are written after the ones inside it. Nothing in the
            // page can name an instance this file does not hold, so they are stepped over rather than used.
            readReferents(cursor, cursor.u32());

            let uri = 0;
            let object = 0;

            return sourceTypes.map((sourceType) => {
                if (sourceType === 1) {
                    const text = uris[uri++] || '(empty)';
                    return { text: text, raw: text, kind: 'string' };
                }

                if (sourceType === 2) return { text: 'instance ' + objects[object++] };

                return { text: '(none)' };
            });
        }

        default:
            throw new Error('property type 0x' + type.toString(16) + ' (' + (TYPE_NAMES[type] || 'unknown')
                + ') is not one this reader knows');
    }
}

/**
 * Reads an array of position-like vectors.
 *
 * @param {Cursor} cursor the chunk.
 * @param {number} count how many vectors.
 * @param {number} components how many floats each vector holds.
 * @returns {string[][]} the formatted components, one array per vector.
 */
function readVectors(cursor, count, components) {
    const parts = [];

    for (let component = 0; component < components; component++) parts.push(columns(cursor, count, 4));

    const values = [];

    for (let index = 0; index < count; index++) {
        values.push(parts.map((part) => num(f32be(part[index]))));
    }

    return values;
}

/**
 * Formats a colour.
 *
 * @param {number} red the red channel, 0 to 1.
 * @param {number} green the green channel, 0 to 1.
 * @param {number} blue the blue channel, 0 to 1.
 * @returns {string} the text.
 */
function rgbText(red, green, blue) {
    return 'rgb(' + Math.round(red * 255) + ', ' + Math.round(green * 255) + ', ' + Math.round(blue * 255) + ')';
}

/**
 * Names one property chunk for the note a failed read leaves behind.
 *
 * @param {Uint8Array} payload the chunk, decompressed.
 * @param {Map<number, string>} classNames the class table read so far.
 * @returns {string} the class, the property and the type, as a reader would say them.
 * @remarks
 * The header it reads is the same one the reader tried to read: a class identifier, a property name and a
 * type byte. A chunk that failed because its header itself is unreadable has no name to give, and saying so
 * is more honest than inventing one.
 */
function propertyLabel(payload, classNames) {
    try {
        const cursor = new Cursor(payload);
        const classId = cursor.u32();
        const property = cursor.str();
        const type = cursor.u8();
        const owner = classNames.get(classId) || 'class ' + classId;

        return owner + '.' + property + ' (' + (TYPE_NAMES[type] || '0x' + type.toString(16)) + ')';
    } catch (error) {
        return 'an unnamed chunk';
    }
}

/**
 * Reads a binary model into an instance list.
 *
 * @param {Uint8Array} bytes the payload.
 * @returns {{ nodes: Object[], classes: string[], truncated: string }} the tree, the class names seen and,
 *     when reading stopped early, the sentence saying where.
 */
function parseBinary(bytes) {
    for (let index = 0; index < BINARY_MAGIC.length; index++) {
        if (bytes[index] !== BINARY_MAGIC[index]) throw new Error('this is not a binary model');
    }

    const cursor = new Cursor(bytes);
    cursor.at = BINARY_MAGIC.length;

    const version = cursor.u16();
    const classCount = cursor.u32();
    const instanceCount = cursor.u32();

    if (version !== 0) throw new Error('binary model version ' + version + ' is not one this reader knows');

    cursor.skip(8);

    const classNames = new Map();
    const instances = [];
    const byClass = new Map();
    const byReferent = new Map();

    let sharedStrings = [];
    const unread = [];

    while (cursor.left >= 16) {
        const name = chunkName(cursor.take(4));
        const compressed = cursor.u32();
        const length = cursor.u32();

        cursor.skip(4);

        if (name === 'END') break;

        const payload = compressed > 0
            ? decompress(cursor.take(compressed), length)
            : cursor.take(length);

        if (name === 'INST') {
            readClass(new Cursor(payload), classNames, instances, byClass, byReferent);
        } else if (name === 'PROP') {
            /*
              A property chunk is independent: it names its class, its property and its type, and the
              chunk's own length says where it ends. One that cannot be read therefore costs its own
              values and nothing else, so it is named and skipped rather than allowed to stop the file. The
              name is what makes the note worth reading: "MaterialVariant.EmissiveMaskContent (Content)"
              says which value is missing and that the reader does not have that type at all.
            */
            try {
                readProperties(new Cursor(payload), byClass, sharedStrings);
            } catch (error) {
                unread.push(propertyLabel(payload, classNames));
            }
        } else if (name === 'PRNT') {
            readParents(new Cursor(payload), byReferent);
        } else if (name === 'SSTR') {
            sharedStrings = readSharedStrings(new Cursor(payload));
        }
    }

    if (instances.length === 0) throw new Error('the model holds no instance table');

    const unmatched = instances.length !== instanceCount;

    let problem = '';

    if (unread.length > 0) {
        const shown = unread.slice(0, 5).join(', ');
        const rest = unread.length > 5 ? ' and ' + (unread.length - 5) + ' more' : '';

        problem = unread.length + (unread.length === 1 ? ' property chunk' : ' property chunks')
            + ' could not be decoded: ' + shown + rest
            + '. The tree is complete and those values are missing.';
    }

    if (unmatched) {
        problem += (problem ? ' ' : '') + 'The header names ' + instanceCount + ' instances and the file holds '
            + instances.length + ', so part of the file was not read.';
    }

    return { nodes: childrenOf(instances), classes: [...classNames.values()], truncated: problem };
}

/**
 * Decompresses a chunk's payload, naming the algorithm when it is one this reader does not have.
 *
 * @param {Uint8Array} payload the stored bytes.
 * @param {number} length the decompressed length.
 * @returns {Uint8Array} the decompressed bytes.
 */
function decompress(payload, length) {
    // Zstandard, which some newer files use where this reader only knows LZ4.
    if (payload.length >= 4 && payload[0] === 0x28 && payload[1] === 0xb5 && payload[2] === 0x2f && payload[3] === 0xfd) {
        throw new Error('the chunk is zstd-compressed and this reader only knows LZ4');
    }

    return decompressLz4(payload, length);
}

/**
 * Reads one class table entry and the instances that belong to it.
 *
 * @param {Cursor} cursor the chunk.
 * @param {Map<number, string>} classNames the class table, filled here.
 * @param {Object[]} instances the instance list, filled here.
 * @param {Map<number, Object[]>} byClass the instances grouped by class, in file order.
 * @param {Map<number, Object>} byReferent the instances keyed by referent.
 * @returns {void}
 */
function readClass(cursor, classNames, instances, byClass, byReferent) {
    // One chunk per class, not one chunk for the file: the count below is that class's instances.
    const classId = cursor.u32();
    const name = cursor.str();
    const format = cursor.u8();
    const count = cursor.u32();

    classNames.set(classId, name);

    const referents = readReferents(cursor, count);

    // Services carry a marker per instance, which is one byte each and says nothing this page shows.
    if (format !== 0) cursor.skip(count);

    const group = byClass.get(classId) || [];
    byClass.set(classId, group);

    for (const referent of referents) {
        const node = {
            className: name,
            attribute: '',
            referent: referent,
            props: new Map(),
            children: []
        };

        instances.push(node);
        group.push(node);
        byReferent.set(referent, node);
    }
}

/**
 * Reads one property chunk and gives its value to every instance of its class.
 *
 * @param {Cursor} cursor the chunk.
 * @param {Map<number, Object[]>} byClass the instances grouped by class.
 * @param {string[]} sharedStrings the shared-string table.
 * @returns {void}
 */
function readProperties(cursor, byClass, sharedStrings) {
    const classId = cursor.u32();
    const name = cursor.str();
    const type = cursor.u8();

    const nodes = byClass.get(classId);

    if (!nodes || nodes.length === 0) return;

    const values = readValues(cursor, type, nodes.length, sharedStrings);
    const label = TYPE_NAMES[type] || 'Type 0x' + type.toString(16);

    for (let index = 0; index < nodes.length; index++) {
        // The type is stamped on the value rather than kept beside it, so the panel can print a column of them
        // without carrying the chunk's header around with every property.
        values[index].type = label;
        nodes[index].props.set(name, values[index]);
    }
}

/**
 * Reads the parent chunk.
 *
 * @param {Cursor} cursor the chunk.
 * @param {Map<number, Object>} byReferent the instances keyed by referent.
 * @returns {void}
 * @remarks
 * The chunk is a version byte, a count, and then two arrays of that length: the children in one and
 * their parents in the other. It is not a run of pairs, which is what this reader assumed, and a run of
 * pairs turns every parent after the first into the child of the one before.
 */
function readParents(cursor, byReferent) {
    cursor.u8();

    const count = cursor.u32();
    const children = readReferents(cursor, count);
    const parents = readReferents(cursor, count);

    for (let index = 0; index < count; index++) {
        const child = byReferent.get(children[index]);

        if (child) child.parent = parents[index];
    }
}

/**
 * Reads the shared-string table.
 *
 * @param {Cursor} cursor the chunk.
 * @returns {string[]} the strings, in index order.
 * @remarks
 * Each entry is the string's MD5 hash and then the string itself. The hash is not needed to read the
 * table, but it is sixteen bytes that have to be stepped over before the string's length.
 */
function readSharedStrings(cursor) {
    cursor.u32();

    const count = cursor.u32();
    const strings = [];

    for (let index = 0; index < count && cursor.left >= 20; index++) {
        cursor.skip(16);
        strings.push(cursor.str());
    }

    return strings;
}

/**
 * Groups instances under their parents and returns the roots.
 *
 * @param {Object[]} instances the flat instance list.
 * @returns {Object[]} the roots.
 */
function childrenOf(instances) {
    const byReferent = new Map();
    for (const node of instances) byReferent.set(node.referent, node);

    const roots = [];

    for (const node of instances) {
        const parent = node.parent === undefined ? null : byReferent.get(node.parent);

        // The parent is kept on the node as well as in the list, because the panel beside the tree names where
        // an instance sits and that name is read by walking up. A node that names itself as its own parent is
        // treated as a root rather than followed, which would otherwise be a loop.
        if (parent && parent !== node) {
            node.parentNode = parent;
            parent.children.push(node);
        } else {
            node.parentNode = null;
            roots.push(node);
        }
    }

    return roots;
}

/**
 * Reads an XML model.
 *
 * @param {string} text the payload as text.
 * @returns {{ nodes: Object[], classes: string[], truncated: string }} the tree.
 */
function parseXml(text) {
    const document_ = new DOMParser().parseFromString(text, 'application/xml');

    if (document_.getElementsByTagName('parsererror').length > 0) throw new Error('the file is not well-formed XML');

    const root = document_.documentElement;

    const toNodes = (element) => {
        const nodes = [];

        for (const item of element.children) {
            if (item.nodeName !== 'Item') continue;

            const node = {
                className: item.getAttribute('class') || 'Item',
                attribute: '',
                referent: item.getAttribute('referent') || '',
                props: new Map(),
                children: []
            };

            for (const property of item.children) {
                if (property.nodeName === 'Item') continue;

                const name = property.getAttribute('name');
                if (!name) continue;

                // In XML the element's own name is the property's type -- a String is written as <string>, a
                // Vector3 as <Vector3> -- which is the same information the binary chunks carry as a byte.
                node.props.set(name, {
                    text: xmlText(property),
                    raw: property.textContent || '',
                    kind: 'string',
                    type: property.nodeName
                });
            }

            node.children = toNodes(item);
            nodes.push(node);
        }

        return nodes;
    };

    const nodes = toNodes(root);

    if (nodes.length === 0) throw new Error('the document holds no items');

    const classes = [];

    // Every instance, not only the roots: the first version of this collected classes from the top level and
    // a model whose root was a single Model reported that one class and nothing inside it.
    (function collect(list) {
        for (const node of list) {
            if (classes.indexOf(node.className) < 0) classes.push(node.className);
            collect(node.children);
        }
    })(nodes);

    return { nodes: nodes, classes: classes, truncated: '' };
}

/**
 * Renders an XML property's value.
 *
 * @param {Element} property the element.
 * @returns {string} the text.
 */
function xmlText(property) {
    if (property.children.length === 0) return (property.textContent || '').trim();

    const parts = [];

    for (const child of property.children) parts.push((child.textContent || '').trim());

    return '(' + parts.join(', ') + ')';
}

/**
 * Counts the instances under a set of roots.
 *
 * @param {Object[]} nodes the roots.
 * @returns {number} the count.
 */
function countNodes(nodes) {
    let total = 0;

    for (const node of nodes) total += 1 + countNodes(node.children);

    return total;
}

/**
 * Reads a node's name, preferring the property that names it.
 *
 * @param {Object} node the node.
 * @returns {string} the name, which may be empty.
 */
function nodeName(node) {
    const property = node.props.get('Name');

    if (property && property.text) return property.text.trim();

    return node.attribute || '';
}

/**
 * Starts the explorer if the page has one.
 *
 * @returns {void}
 */
export function startExplorer() {
    const root = document.querySelector('[data-explorer]');

    if (!root) return;

    const identifier = root.getAttribute('data-model');
    const endpoint = root.getAttribute('data-endpoint') || '/Api/Download.ashx';

    if (!identifier) {
        showMessage(root, 'danger', 'The page named no model.', 'Nothing was asked for, so nothing was fetched.');
        return;
    }

    let response;

    fetch(endpoint + '?id=' + encodeURIComponent(identifier), { credentials: 'same-origin' })
        .then((answer) => {
            response = answer;

            if (answer.redirected || /text\/html/i.test(answer.headers.get('content-type') || '')) {
                // The handler redirects a failed fetch to the model page, which explains the reason. Following it
                // would put a page of markup here, so the answer is stopped and the sentence points at it.
                throw new Error('the asset could not be fetched; the model page says why');
            }

            if (!answer.ok) throw new Error('the handler answered ' + answer.status);

            return answer.arrayBuffer();
        })
        .then((buffer) => {
            const bytes = new Uint8Array(buffer);
            const kind = detect(bytes);

            if (!kind) {
                showMessage(
                    root,
                    'warning',
                    'This asset is not a model.',
                    'The payload is ' + (response.headers.get('content-type') || 'of an unknown kind')
                    + ', which the explorer cannot read. Decals, audio and images are stored the same way a model '
                    + 'is, and are served by the same link.');
                return;
            }

            let model;

            try {
                model = kind === 'binary' ? parseBinary(bytes) : parseXml(decoder.decode(bytes));
            } catch (error) {
                showMessage(
                    root,
                    'warning',
                    'The payload could not be read as a model.',
                    error.message + '. The file itself is still downloadable from the model page.');
                return;
            }

            draw(root, model, bytes.length);
        })
        .catch((error) => {
            showMessage(root, 'warning', 'The model could not be fetched.', error.message + '.');
        });
}

/**
 * Decides which of the two formats a payload is.
 *
 * @param {Uint8Array} bytes the payload.
 * @returns {string} "binary", "xml", or an empty string for anything else.
 */
function detect(bytes) {
    for (let index = 0; index < BINARY_MAGIC.length; index++) {
        if (bytes[index] !== BINARY_MAGIC[index]) return looksLikeXml(bytes) ? 'xml' : '';
    }

    return 'binary';
}

/**
 * Reads the payload's first non-space character to see whether it is markup.
 *
 * @param {Uint8Array} bytes the payload.
 * @returns {boolean} whether it looks like XML.
 */
function looksLikeXml(bytes) {
    for (let index = 0; index < Math.min(bytes.length, 64); index++) {
        const byte = bytes[index];

        if (byte === 32 || byte === 9 || byte === 10 || byte === 13) continue;

        return byte === 60;
    }

    return false;
}

/**
 * Replaces the explorer's contents with a message.
 *
 * @param {Element} root the explorer.
 * @param {string} severity the alert's severity.
 * @param {string} title the title.
 * @param {string} body the sentence.
 * @returns {void}
 */
function showMessage(root, severity, title, body) {
    while (root.firstChild) root.removeChild(root.firstChild);

    const alert = document.createElement('div');
    alert.className = 'alert alert-' + severity;
    alert.setAttribute('role', 'alert');

    const heading = document.createElement('strong');
    heading.textContent = title;

    const text = document.createElement('p');
    text.textContent = body;

    alert.appendChild(heading);
    alert.appendChild(text);
    root.appendChild(alert);
}

/*
  ------------------------------------------------------------------------------------------------
  THE VIEW

  A tree of instances is read the way the file is shaped: a row per instance, nested under its parent.
  That shape is not worth inventing, so the view borrows the one a reader of these files already knows --
  a caret, a mark for what kind of thing the row is, the instance's name, and its class set faintly at
  the end. The class is the part that tells a reader what a row is when the name does not, so it is
  always shown rather than hidden behind a hover.
  ------------------------------------------------------------------------------------------------
*/

/*
  The families the marks stand for. Roblox has hundreds of classes and a handful of kinds: code, a
  physical part, an interface element, a stored value, something that makes sound, a container. A class
  that is not named here falls through to a rule that reads its name, and anything left over is drawn as
  a plain circle.
*/
const CLASS_FAMILIES = {
    Script: 'script', LocalScript: 'script', ModuleScript: 'script',
    Folder: 'folder', Model: 'folder', Configuration: 'folder', Actor: 'folder',
    ScreenGui: 'gui', SurfaceGui: 'gui', BillboardGui: 'gui', Frame: 'gui', TextLabel: 'gui',
    TextButton: 'gui', TextBox: 'gui', ImageLabel: 'gui', ImageButton: 'gui', ScrollingFrame: 'gui',
    ViewportFrame: 'gui', UIListLayout: 'gui', UIGridLayout: 'gui', UIPadding: 'gui',
    Part: 'part', MeshPart: 'part', WedgePart: 'part', CornerWedgePart: 'part', TrussPart: 'part',
    Seat: 'part', VehicleSeat: 'part', SpawnLocation: 'part', UnionOperation: 'part',
    NegateOperation: 'part', Terrain: 'part',
    Sound: 'sound', SoundGroup: 'sound',
    StringValue: 'value', NumberValue: 'value', BoolValue: 'value', IntValue: 'value',
    ObjectValue: 'value', CFrameValue: 'value', Vector3Value: 'value', Color3Value: 'value',
    BrickColorValue: 'value', RayValue: 'value', NumberSequence: 'value', ColorSequence: 'value'
};

/**
 * Picks the family a class belongs to.
 *
 * @param {string} className the class name.
 * @returns {string} the family, which is also the mark's class name.
 */
function familyOf(className) {
    if (CLASS_FAMILIES[className]) return CLASS_FAMILIES[className];
    if (/(Script|Module)$/.test(className)) return 'script';
    if (/Value$/.test(className)) return 'value';
    if (/(Gui|Frame|Label|Button|TextBox|Image|Layout|Padding)$/.test(className)) return 'gui';
    if (/(Part|Wedge|Seat|Mesh|Terrain)$/.test(className)) return 'part';
    if (/(Sound)$/.test(className)) return 'sound';
    return 'object';
}

/**
 * Builds the mark that stands for a class.
 *
 * @param {string} className the class name.
 * @param {boolean} [large] whether this is the panel's larger copy.
 * @returns {HTMLSpanElement} the element.
 */
function icon(className, large) {
    const mark = document.createElement('span');
    mark.className = 'explorer-icon explorer-icon-' + familyOf(className) + (large ? ' explorer-icon-lg' : '');
    mark.setAttribute('aria-hidden', 'true');
    return mark;
}

/**
 * Turns a count and a word into a phrase.
 *
 * @param {number} count the count.
 * @param {string} singular the word for one.
 * @param {string} [plural] the word for any other number, when it is not the singular with an s.
 * @returns {string} the phrase.
 */
function plural(count, singular, plural) {
    const word = plural || (singular.charAt(singular.length - 1) === 'y'
        ? singular.slice(0, -1) + 'ies'
        : singular + 's');

    return count.toLocaleString() + ' ' + (count === 1 ? singular : word);
}

/**
 * Formats a byte count.
 *
 * @param {number} size the count.
 * @returns {string} the text.
 */
function sizeText(size) {
    if (size < 1024) return size + ' B';
    if (size < 1024 * 1024) return (size / 1024).toFixed(1) + ' KB';

    return (size / (1024 * 1024)).toFixed(1) + ' MB';
}

/**
 * Names where an instance sits, as a trail from the root.
 *
 * @param {Object} node the instance.
 * @returns {string[]} the names, root first.
 */
function pathOf(node) {
    const trail = [];
    let current = node;

    while (current) {
        trail.unshift(nodeName(current) || current.className);
        current = current.parentNode;
    }

    return trail;
}

/**
 * Creates an element.
 *
 * @param {string} tag the element's name.
 * @param {string} className the class, or an empty string.
 * @param {string} [text] the text, when it has any.
 * @returns {HTMLElement} the element.
 */
function el(tag, className, text) {
    const element = document.createElement(tag);

    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;

    return element;
}

/**
 * Says whether an instance is a script.
 *
 * @param {Object} node the instance.
 * @returns {boolean} whether its source can be read.
 * @remarks
 * The class check is the one that matters, because those three classes are the whole of
 * <c>LuaSourceContainer</c> in a model file. The property check behind it covers a file written by
 * something that gave the class a different spelling but still wrote a Source.
 */
function isSourceNode(node) {
    return SOURCE_CLASSES.has(node.className) || node.props.has('Source');
}

/**
 * Reads an instance's source.
 *
 * @param {Object} node the instance.
 * @returns {string} the text, which is empty when the file stores none.
 */
function sourceOf(node) {
    const property = node.props.get('Source');

    return property && typeof property.text === 'string' ? property.text : '';
}

/**
 * Draws the tree, its chrome and the property panel.
 *
 * @param {Element} root the explorer.
 * @param {{ nodes: Object[], classes: string[], truncated: string }} model the parsed tree.
 * @param {number} size the payload's length in bytes.
 * @returns {void}
 */
function draw(root, model, size) {
    while (root.firstChild) root.removeChild(root.firstChild);

    const total = countNodes(model.nodes);

    const window_ = el('div', 'explorer-window');

    const titlebar = el('div', 'explorer-titlebar');
    titlebar.appendChild(el('span', 'explorer-title', 'Explorer'));
    titlebar.appendChild(el('span', 'explorer-titlebar-count', sizeText(size)));

    const bar = el('div', 'explorer-toolbar');

    const field = document.createElement('input');
    field.type = 'search';
    field.className = 'explorer-input';
    field.placeholder = 'Filter by name or class';
    field.setAttribute('aria-label', 'Filter instances by name or class');

    const count = el('span', 'explorer-count',
        plural(total, 'instance') + ' \u00b7 ' + plural(model.classes.length, 'class', 'classes'));

    const actions = el('div', 'explorer-actions');

    bar.appendChild(field);
    bar.appendChild(count);
    bar.appendChild(actions);

    const layout = el('div', 'explorer-layout');

    const tree = document.createElement('ul');
    tree.className = 'explorer-tree';
    tree.setAttribute('role', 'tree');

    const details = el('div', 'explorer-details');
    details.appendChild(hint('Select an instance to read its properties.'));

    const state = {
        shell: window_,
        layout: layout,
        modal: null,
        tabsElement: null,
        code: null,
        tabs: [],
        active: null,
        home: null,
        copyText: '',
        escape: null
    };

    const entries = [];

    for (const node of model.nodes) tree.appendChild(buildNode(node, 0, entries, state, select));

    /*
      Selection is one function for the whole view rather than a listener per row, because it has to do two
      things at once: paint the row and fill the panel. When rows are drawn with a listener that closes over
      the row itself, filtering can leave the painted row hidden while the panel shows it, and the reader is
      then looking at the properties of something they cannot see.
    */
    function select(row, node) {
        const previous = tree.querySelector('.explorer-row.is-selected');

        if (previous) previous.classList.remove('is-selected');

        row.classList.add('is-selected');
        showDetails(state, details, node);

        // A script is clicked for its code, so it opens rather than waiting to be asked twice. Its
        // properties are still a row away in the frame that opens.
        if (isSourceNode(node)) openSource(state, node);
    }

    const expand = action('Expand all');
    const collapse = action('Collapse all');

    expand.addEventListener('click', () => {
        for (const entry of entries) entry.item.classList.remove('explorer-collapsed');
    });

    collapse.addEventListener('click', () => {
        for (const entry of entries) {
            if (entry.depth > 0) entry.item.classList.add('explorer-collapsed');
        }
    });

    actions.appendChild(expand);
    actions.appendChild(collapse);

    layout.appendChild(tree);
    layout.appendChild(details);

    window_.appendChild(titlebar);
    window_.appendChild(bar);
    window_.appendChild(layout);

    root.appendChild(window_);

    if (model.truncated) {
        const note = el('p', 'explorer-note', model.truncated);
        root.appendChild(note);
    }

    let pending = 0;

    field.addEventListener('input', () => {
        // A model can be a few thousand rows, and filtering walks every one of them. One pass per keystroke is
        // fine; one pass per character when a whole string is pasted at once is not, so the term is applied on
        // the next frame instead of during the event.
        if (pending) return;

        pending = window.requestAnimationFrame(() => {
            pending = 0;
            applyFilter(tree, field.value.trim().toLowerCase(), count, total, model.classes.length);
        });
    });

    tree.addEventListener('keydown', (event) => {
        if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;

        // Moving between rows without a mouse is the difference between a list and a tree for a reader who
        // cannot use one. The visible rows in document order are the rows they can reach.
        const rows = [...tree.querySelectorAll('.explorer-row')].filter((row) => row.offsetParent !== null);
        const at = rows.indexOf(document.activeElement);

        if (at < 0) return;

        event.preventDefault();

        const next = rows[Math.max(0, Math.min(rows.length - 1, at + (event.key === 'ArrowDown' ? 1 : -1)))];
        next.focus();
    });
}

/**
 * Builds a small bar button.
 *
 * @param {string} text the label.
 * @returns {HTMLButtonElement} the button.
 */
function action(text) {
    const element = document.createElement('button');
    element.type = 'button';
    element.className = 'explorer-action';
    element.textContent = text;
    return element;
}

/**
 * Renders a small sentence.
 *
 * @param {string} text the sentence.
 * @returns {Element} the element.
 */
function hint(text) {
    return el('p', 'explorer-hint', text);
}

/**
 * Builds one node of the tree, and its descendants.
 *
 * @param {Object} node the node.
 * @param {number} depth how deep it sits.
 * @param {Object[]} entries the flat list of rendered rows, filled here for bulk actions.
 * @param {Object} state the view's state, for the source frame.
 * @param {Function} select the view's selection function.
 * @returns {HTMLLIElement} the item.
 */
function buildNode(node, depth, entries, state, select) {
    const item = document.createElement('li');
    item.className = 'explorer-node' + (depth >= 3 ? ' explorer-collapsed' : '');
    item.dataset.className = node.className.toLowerCase();
    item.dataset.name = (nodeName(node) || '').toLowerCase();

    const row = el('div', 'explorer-row');
    row.tabIndex = 0;
    row.setAttribute('role', 'treeitem');

    const arrow = el('span', 'explorer-arrow' + (node.children.length === 0 ? ' is-leaf' : ''));

    if (node.children.length > 0) {
        arrow.addEventListener('click', (event) => {
            event.stopPropagation();
            item.classList.toggle('explorer-collapsed');
        });
    }

    const name = el('span', 'explorer-name', nodeName(node) || '\u2014');
    const className = el('span', 'explorer-class', node.className);

    row.appendChild(arrow);
    row.appendChild(icon(node.className));
    row.appendChild(name);
    row.appendChild(className);

    row.addEventListener('click', () => select(row, node));

    row.addEventListener('dblclick', () => {
        if (isSourceNode(node)) openSource(state, node);
        else if (node.children.length > 0) item.classList.toggle('explorer-collapsed');
    });

    row.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            select(row, node);
            return;
        }

        if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
            event.preventDefault();

            const collapsed = item.classList.contains('explorer-collapsed');

            if (event.key === 'ArrowRight' && collapsed) item.classList.remove('explorer-collapsed');
            if (event.key === 'ArrowLeft' && !collapsed) item.classList.add('explorer-collapsed');
        }
    });

    item.appendChild(row);
    entries.push({ item: item, row: row, depth: depth });

    if (node.children.length > 0) {
        const list = document.createElement('ul');
        list.className = 'explorer-children';
        list.setAttribute('role', 'group');

        for (const child of node.children) list.appendChild(buildNode(child, depth + 1, entries, state, select));

        item.appendChild(list);
    }

    return item;
}

/**
 * Fills the panel with one instance.
 *
 * @param {Object} state the view's state.
 * @param {Element} details the panel.
 * @param {Object} node the instance.
 * @returns {void}
 */
function showDetails(state, details, node) {
    while (details.firstChild) details.removeChild(details.firstChild);

    const head = el('div', 'explorer-head');

    const titles = el('div', 'explorer-head-text');

    const title = el('h4', 'explorer-details-title', nodeName(node) || '(unnamed ' + node.className + ')');

    const crumbs = el('p', 'explorer-crumbs', pathOf(node).join(' \u203a '));

    titles.appendChild(title);
    titles.appendChild(crumbs);

    head.appendChild(icon(node.className, true));
    head.appendChild(titles);

    if (isSourceNode(node)) {
        const open = el('button', 'explorer-open', 'Source');
        open.type = 'button';
        open.title = 'Open the source in a larger frame';
        open.addEventListener('click', () => openSource(state, node));
        head.appendChild(open);
    }

    details.appendChild(head);

    const descendants = countNodes(node.children);

    const meta = el('p', 'explorer-meta', node.className
        + (node.referent === '' || node.referent === undefined ? '' : ' \u00b7 referent ' + node.referent)
        + ' \u00b7 ' + plural(node.props.size, 'property', 'properties')
        + (descendants > 0 ? ' \u00b7 ' + plural(descendants, 'descendant') : ''));

    details.appendChild(meta);

    if (node.props.size === 0) {
        details.appendChild(hint('This class carries no properties in the file.'));
        return;
    }

    const table = el('table', 'explorer-props');

    const headRow = document.createElement('tr');
    const headings = ['Property', 'Type', 'Value'];

    for (const heading of headings) {
        const cell = document.createElement('th');
        cell.scope = 'col';
        cell.textContent = heading;
        headRow.appendChild(cell);
    }

    const head_ = document.createElement('thead');
    head_.appendChild(headRow);
    table.appendChild(head_);

    const body_ = document.createElement('tbody');

    for (const name of [...node.props.keys()].sort()) {
        const property = node.props.get(name);

        const row = document.createElement('tr');

        const label = el('td', 'explorer-prop-name', name);
        const type = el('td', 'explorer-prop-type', property.type || '');
        const value = el('td', 'explorer-prop-value');

        if (name === 'Source' || (property.kind === 'string' && property.text.length > PROPERTY_TEXT_LIMIT)) {
            const pre = el('pre', 'explorer-source');
            pre.textContent = property.text.slice(0, SOURCE_LIMIT)
                + (property.text.length > SOURCE_LIMIT ? '\n\n[cut here]' : '');
            value.appendChild(pre);

            if (name === 'Source') value.appendChild(copy(property.text));
        } else {
            const text = property.text.length > PROPERTY_TEXT_LIMIT
                ? property.text.slice(0, PROPERTY_TEXT_LIMIT) + '\u2026'
                : property.text;
            value.textContent = text;
        }

        row.appendChild(label);
        row.appendChild(type);
        row.appendChild(value);
        body_.appendChild(row);
    }

    table.appendChild(body_);
    details.appendChild(table);
}

/**
 * Builds a control that puts a value on the clipboard.
 *
 * @param {string} text the value.
 * @returns {HTMLButtonElement} the control.
 * @remarks
 * A script's source is the reason this exists: it is the one value long enough that a reader wants it
 * somewhere else rather than on this page.
 */
function copy(text) {
    const control = el('button', 'explorer-copy', 'Copy source');
    control.type = 'button';

    control.addEventListener('click', () => {
        copyText(text, (label) => {
            control.textContent = label;
            window.setTimeout(() => { control.textContent = 'Copy source'; }, 1400);
        });
    });

    return control;
}

/**
 * Puts text on the clipboard, reporting what happened.
 *
 * @param {string} text the text.
 * @param {Function} done receives the word to show the reader.
 * @returns {void}
 */
function copyText(text, done) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(
            () => done('Copied'),
            () => done('Copy failed'));
        return;
    }

    // A page served over plain HTTP has no clipboard object, so the selection route is kept for it: it is
    // what the reader would do by hand, and it works where the modern call does not.
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', 'readonly');
    area.className = 'explorer-clip';

    document.body.appendChild(area);
    area.select();

    try {
        done(document.execCommand('copy') ? 'Copied' : 'Copy failed');
    } catch (error) {
        done('Copy failed');
    }

    document.body.removeChild(area);
}

/**
 * Builds the source frame the first time a script is opened.
 *
 * @param {Object} state the view's state, filled with the frame's parts.
 * @returns {void}
 */
function ensureViewer(state) {
    if (state.modal) return;

    const modal = el('div', 'source-modal');

    const frame = el('div', 'source-frame');
    frame.setAttribute('role', 'dialog');
    frame.setAttribute('aria-modal', 'true');
    frame.setAttribute('aria-label', 'Script source');

    const head = el('div', 'source-head');
    const tabs = el('div', 'source-tabs');
    const tools = el('div', 'source-tools');

    const copyButton = el('button', 'source-tool', 'Copy');
    copyButton.type = 'button';
    copyButton.title = 'Copy the active script';
    copyButton.addEventListener('click', () => {
        copyText(state.copyText, (label) => {
            copyButton.textContent = label;
            window.setTimeout(() => { copyButton.textContent = 'Copy'; }, 1400);
        });
    });

    const closeButton = el('button', 'source-tool', 'Close');
    closeButton.type = 'button';
    closeButton.title = 'Close the source frame';
    closeButton.addEventListener('click', () => closeViewer(state));

    tools.appendChild(copyButton);
    tools.appendChild(closeButton);

    head.appendChild(tabs);
    head.appendChild(tools);

    const code = el('div', 'source-code');
    const side = el('div', 'source-side');

    frame.appendChild(head);
    frame.appendChild(code);
    frame.appendChild(side);
    modal.appendChild(frame);

    // The dark area around the frame closes it; a click inside the frame does not.
    modal.addEventListener('mousedown', (event) => {
        if (event.target === modal) closeViewer(state);
    });

    state.escape = (event) => {
        if (event.key === 'Escape') closeViewer(state);
    };

    document.addEventListener('keydown', state.escape);
    document.body.appendChild(modal);
    document.body.style.overflow = 'hidden';

    state.modal = modal;
    state.tabsElement = tabs;
    state.code = code;
    state.copyButton = copyButton;

    // The window is moved, not copied: one tree, one filter, one selection, in both places.
    state.home = state.shell.parentNode;
    side.appendChild(state.shell);
}

/**
 * Builds the tab for one script.
 *
 * @param {Object} state the view's state.
 * @param {Object} node the script.
 * @returns {HTMLDivElement} the tab.
 */
function sourceTab(state, node) {
    const button = el('div', 'source-tab');
    button.setAttribute('role', 'tab');

    const label = el('span', 'source-tab-label', nodeName(node) || node.className);

    const close = el('button', 'source-tab-close', '\u00d7');
    close.type = 'button';
    close.title = 'Close this script';
    close.addEventListener('click', (event) => {
        event.stopPropagation();
        closeTab(state, node);
    });

    button.appendChild(label);
    button.appendChild(close);
    button.addEventListener('click', () => openSource(state, node));

    return button;
}

/**
 * Opens a script in the source frame, adding a tab when it is not already open.
 *
 * @param {Object} state the view's state.
 * @param {Object} node the script.
 * @returns {void}
 */
function openSource(state, node) {
    ensureViewer(state);

    let tab = state.tabs.find((entry) => entry.node === node);

    if (!tab) {
        tab = { node: node, button: sourceTab(state, node) };
        state.tabs.push(tab);
        state.tabsElement.appendChild(tab.button);
    }

    activateSource(state, tab);
}

/**
 * Makes one tab the shown one.
 *
 * @param {Object} state the view's state.
 * @param {Object} tab the tab.
 * @returns {void}
 */
function activateSource(state, tab) {
    for (const entry of state.tabs) entry.button.classList.toggle('is-active', entry === tab);

    state.active = tab;
    state.copyText = sourceOf(tab.node);

    paintSource(state.code, state.copyText);
}

/**
 * Closes one tab, and the frame when it was the last.
 *
 * @param {Object} state the view's state.
 * @param {Object} node the script the tab belongs to.
 * @returns {void}
 */
function closeTab(state, node) {
    const index = state.tabs.findIndex((entry) => entry.node === node);

    if (index < 0) return;

    const tab = state.tabs[index];
    tab.button.remove();
    state.tabs.splice(index, 1);

    if (state.active !== tab) return;

    const next = state.tabs[index] || state.tabs[index - 1] || null;

    state.active = null;

    if (next) activateSource(state, next);
    else closeViewer(state);
}

/**
 * Closes the source frame and puts the explorer back on the page.
 *
 * @param {Object} state the view's state.
 * @returns {void}
 */
function closeViewer(state) {
    if (!state.modal) return;

    document.removeEventListener('keydown', state.escape);
    state.modal.remove();
    state.modal = null;
    document.body.style.overflow = '';

    if (state.home) state.home.insertBefore(state.shell, state.home.firstChild);

    state.home = null;
    state.active = null;
    state.tabsElement = null;
    state.code = null;
    state.copyText = '';

    for (const tab of state.tabs) tab.button.remove();

    state.tabs = [];
}

/**
 * Draws a script's source with its line numbers.
 *
 * @param {Element} code the frame's code area.
 * @param {string} text the source.
 * @returns {void}
 */
function paintSource(code, text) {
    while (code.firstChild) code.removeChild(code.firstChild);

    if (!text) {
        code.appendChild(el('p', 'source-note', 'This script carries no source in the file.'));
        return;
    }

    const shown = text.length > SOURCE_LIMIT ? text.slice(0, SOURCE_LIMIT) : text;
    const lines = splitSource(tokenizeLuau(shown));
    const fragment = document.createDocumentFragment();

    for (let index = 0; index < lines.length; index++) {
        const row = el('div', 'source-line');

        row.appendChild(el('span', 'source-no', String(index + 1)));

        const body = el('span', 'source-txt');

        for (const token of lines[index]) {
            if (token.type === 'plain') body.appendChild(document.createTextNode(token.text));
            else body.appendChild(el('span', 'source-' + token.type, token.text));
        }

        if (body.childNodes.length === 0) body.textContent = ' ';

        row.appendChild(body);
        fragment.appendChild(row);
    }

    code.appendChild(fragment);

    if (text.length > SOURCE_LIMIT) {
        code.appendChild(el('p', 'source-note', 'The source is longer than this frame shows at once; it is cut at '
            + SOURCE_LIMIT.toLocaleString() + ' characters.'));
    }
}

/* -----------------------------------------------------------------------------------------------
   Reading the script's language, for the colours only.
   ----------------------------------------------------------------------------------------------- */

const LUAU_KEYWORDS = new Set([
    'and', 'break', 'do', 'else', 'elseif', 'end', 'export', 'false', 'for', 'function', 'if', 'in',
    'local', 'nil', 'not', 'or', 'repeat', 'return', 'then', 'true', 'type', 'until', 'while', 'continue'
]);

const LUAU_GLOBALS = new Set([
    'game', 'workspace', 'script', 'plugin', 'shared', 'math', 'string', 'table', 'task', 'os',
    'coroutine', 'utf8', 'bit32', 'debug', 'buffer', 'vector', 'typeof', 'require', 'load', 'loadstring',
    'dofile', 'print', 'warn', 'error', 'assert', 'pcall', 'xpcall', 'pairs', 'ipairs', 'next', 'select',
    'tonumber', 'tostring', 'type', 'rawget', 'rawset', 'rawequal', 'rawlen', 'setmetatable',
    'getmetatable', 'unpack', 'newproxy', 'tick', 'wait', 'spawn', 'delay', 'collectgarbage', 'gcinfo'
]);

const OPERATORS = '+-*/%^#=~<>.,;:(){}[]';

const NUMBER_LITERAL = /^(?:0[xX][0-9a-fA-F]+|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)/;

/**
 * Finds the end of a long bracket, the `[[...]]` form.
 *
 * @param {string} text the source.
 * @param {number} at the `[` that starts it.
 * @returns {number} one past the closing bracket, or -1 when this is not one.
 * @remarks
 * The level is the number of `=` between the brackets, which is how a string is allowed to hold the
 * terminator that would otherwise end it.
 */
function longBracket(text, at) {
    if (text.charAt(at) !== '[') return -1;

    let level = 0;

    while (text.charAt(at + 1 + level) === '=') level += 1;

    if (text.charAt(at + 1 + level) !== '[') return -1;

    const terminator = ']' + '='.repeat(level) + ']';
    const close = text.indexOf(terminator, at + 2 + level);

    return close < 0 ? text.length : close + terminator.length;
}

/**
 * Splits Luau source into coloured runs.
 *
 * @param {string} text the source.
 * @returns {Object[]} the runs, in order, with a type and the text it covers.
 * @remarks
 * Enough of the language to colour a page: comments, strings, numbers, words and operators. It is not a
 * parser and does not try to be one -- nothing here decides what the script does, only which run a
 * character belongs to.
 */
function tokenizeLuau(text) {
    const tokens = [];
    let plain = '';
    let at = 0;

    const flush = () => {
        if (plain) {
            tokens.push({ type: 'plain', text: plain });
            plain = '';
        }
    };

    const push = (type, value) => {
        flush();
        tokens.push({ type: type, text: value });
    };

    while (at < text.length) {
        const here = text.charAt(at);
        const next = text.charAt(at + 1);

        if (here === '-' && next === '-') {
            const long = longBracket(text, at + 2);

            if (long >= 0) {
                push('comment', text.slice(at, long));
                at = long;
                continue;
            }

            let end = text.indexOf('\n', at);

            if (end < 0) end = text.length;

            push('comment', text.slice(at, end));
            at = end;
            continue;
        }

        if (here === '[') {
            const long = longBracket(text, at);

            if (long >= 0) {
                push('string', text.slice(at, long));
                at = long;
                continue;
            }
        }

        if (here === '"' || here === "'") {
            let end = at + 1;

            while (end < text.length && text.charAt(end) !== here && text.charAt(end) !== '\n') {
                // A backslash escapes the next character, which may be the quote itself.
                if (text.charAt(end) === '\\') end += 1;
                end += 1;
            }

            if (text.charAt(end) === here) end += 1;

            push('string', text.slice(at, end));
            at = end;
            continue;
        }

        if (/[0-9]/.test(here) || (here === '.' && /[0-9]/.test(next))) {
            const literal = NUMBER_LITERAL.exec(text.slice(at, at + 64));

            if (literal) {
                push('number', literal[0]);
                at += literal[0].length;
                continue;
            }
        }

        if (/[A-Za-z_]/.test(here)) {
            let end = at + 1;

            while (end < text.length && /[A-Za-z0-9_]/.test(text.charAt(end))) end += 1;

            const word = text.slice(at, end);

            push(LUAU_KEYWORDS.has(word) ? 'keyword' : LUAU_GLOBALS.has(word) ? 'global' : 'plain', word);
            at = end;
            continue;
        }

        if (OPERATORS.indexOf(here) >= 0) {
            push('operator', here);
            at += 1;
            continue;
        }

        plain += here;
        at += 1;
    }

    flush();

    return tokens;
}

/**
 * Breaks coloured runs at line ends.
 *
 * @param {Object[]} tokens the runs.
 * @returns {Object[][]} one list of runs per line.
 */
function splitSource(tokens) {
    const lines = [[]];

    for (const token of tokens) {
        const parts = token.text.split('\n');

        for (let index = 0; index < parts.length; index++) {
            if (index > 0) lines.push([]);
            if (parts[index]) lines[lines.length - 1].push({ type: token.type, text: parts[index] });
        }
    }

    return lines;
}

/**
 * Hides the rows that do not match, and opens the ones that do.
 *
 * @param {Element} tree the tree's list.
 * @param {string} term the lower-cased term.
 * @param {Element} count the element that states how much of the tree is shown.
 * @param {number} total how many instances the model holds.
 * @param {number} classes how many classes the model holds.
 * @returns {boolean} whether anything under this list matched.
 */
function applyFilter(tree, term, count, total, classes) {
    if (!term) {
        for (const item of tree.querySelectorAll('.explorer-node')) item.hidden = false;

        count.textContent = plural(total, 'instance') + ' \u00b7 ' + plural(classes, 'class', 'classes');
        count.classList.remove('is-empty');

        return true;
    }

    let shown = 0;

    const hide = (list) => {
        let any = false;

        for (const item of list.children) {
            if (!item.classList.contains('explorer-node')) continue;

            const self = item.dataset.className.indexOf(term) >= 0 || item.dataset.name.indexOf(term) >= 0;
            const children = item.querySelector('.explorer-children');
            const inside = children ? hide(children) : false;

            item.hidden = !(self || inside);

            if (self || inside) {
                shown += 1;
                item.classList.remove('explorer-collapsed');
                any = true;
            }
        }

        return any;
    };

    hide(tree);

    const matched = total - shown;

    count.textContent = shown === 0
        ? 'Nothing matches'
        : matched + ' hidden \u00b7 ' + shown.toLocaleString() + ' shown';

    count.classList.toggle('is-empty', shown === 0);

    return shown > 0;
}
