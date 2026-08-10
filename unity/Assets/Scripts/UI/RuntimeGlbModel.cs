using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Bounded GLB 2.0 reader used only by the isolated world-map presentation
    /// path. Embedded geometry, PNG/JPEG textures and PBR colour/emission are
    /// supported; external URIs, scripts and arbitrary extensions are rejected.
    /// Imported content therefore cannot execute code or touch Memory.
    /// </summary>
    public static class RuntimeGlbModel
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;
        private const int MaxNodes = 512;
        private const int MaxMeshes = 256;
        private const int MaxVertices = 250000;
        private const int MaxTriangles = 350000;

        private sealed class Parsed
        {
            public JObject Root;
            public byte[] Bin;
        }

        public static bool TryValidate(byte[] bytes, out string error)
        {
            error = string.Empty;
            if (!TryParse(bytes, out Parsed parsed, out error)) return false;
            try
            {
                JArray nodes = (JArray)parsed.Root["nodes"] ?? new JArray();
                JArray meshes = (JArray)parsed.Root["meshes"] ?? new JArray();
                if (nodes.Count > MaxNodes || meshes.Count > MaxMeshes)
                {
                    error = "glb_scene_unbounded";
                    return false;
                }
                int vertices = 0;
                int triangles = 0;
                foreach (JToken mesh in meshes)
                {
                    foreach (JToken primitive in
                        (JArray)mesh["primitives"] ?? new JArray())
                    {
                        JObject attributes = primitive["attributes"] as JObject;
                        if (attributes == null || attributes["POSITION"] == null)
                        {
                            error = "glb_position_missing";
                            return false;
                        }
                        int positionAccessor = attributes.Value<int>("POSITION");
                        Vector3[] decodedPositions = ReadVector3Accessor(
                            parsed, positionAccessor, true);
                        int vertexCount = decodedPositions.Length;
                        int indexCount;
                        if (primitive["indices"] == null)
                            indexCount = vertexCount;
                        else
                            indexCount = ReadIndices(
                                parsed,
                                primitive.Value<int>("indices")).Length;
                        if (attributes["NORMAL"] != null &&
                            ReadVector3Accessor(
                                parsed,
                                attributes.Value<int>("NORMAL"),
                                true).Length != vertexCount)
                        {
                            error = "glb_normal_count_mismatch";
                            return false;
                        }
                        if (attributes["TEXCOORD_0"] != null &&
                            ReadVector2Accessor(
                                parsed,
                                attributes.Value<int>("TEXCOORD_0")).Length !=
                            vertexCount)
                        {
                            error = "glb_uv_count_mismatch";
                            return false;
                        }
                        if (primitive["mode"] != null &&
                            primitive.Value<int>("mode") != 4)
                        {
                            error = "glb_primitive_mode_unsupported";
                            return false;
                        }
                        vertices += vertexCount;
                        triangles += indexCount / 3;
                    }
                }
                if (vertices <= 0 || vertices > MaxVertices ||
                    triangles <= 0 || triangles > MaxTriangles)
                {
                    error = "glb_geometry_unbounded";
                    return false;
                }
                foreach (JToken buffer in
                    (JArray)parsed.Root["buffers"] ?? new JArray())
                {
                    string uri = buffer.Value<string>("uri");
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        error = "glb_external_buffer_forbidden";
                        return false;
                    }
                }
                foreach (JToken image in
                    (JArray)parsed.Root["images"] ?? new JArray())
                {
                    string uri = image.Value<string>("uri");
                    if (!string.IsNullOrWhiteSpace(uri) &&
                        !uri.StartsWith("data:image/", StringComparison.Ordinal))
                    {
                        error = "glb_external_image_forbidden";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "glb_contract:" + ex.GetType().Name;
                return false;
            }
        }

        public static bool TryInstantiate(
            byte[] bytes,
            Transform parent,
            Shader hologramShader,
            out GameObject rootObject,
            out string error)
        {
            rootObject = null;
            if (parent == null)
            {
                error = "glb_parent_missing";
                return false;
            }
            if (!TryValidate(bytes, out error) ||
                !TryParse(bytes, out Parsed parsed, out error))
                return false;
            try
            {
                var materialCache = BuildMaterials(parsed, hologramShader);
                var meshCache = new Dictionary<int, List<GameObject>>();
                rootObject = new GameObject("Imported GLB");
                rootObject.transform.SetParent(parent, false);
                JArray scenes = (JArray)parsed.Root["scenes"];
                int sceneIndex = parsed.Root.Value<int?>("scene") ?? 0;
                if (scenes == null || sceneIndex < 0 || sceneIndex >= scenes.Count)
                    throw new InvalidOperationException("scene_missing");
                foreach (JToken nodeIndex in
                    (JArray)scenes[sceneIndex]["nodes"] ?? new JArray())
                    BuildNode(
                        parsed,
                        nodeIndex.Value<int>(),
                        rootObject.transform,
                        materialCache,
                        meshCache,
                        new HashSet<int>());
                return rootObject.transform.childCount > 0;
            }
            catch (Exception ex)
            {
                if (rootObject != null)
                    UnityEngine.Object.Destroy(rootObject);
                rootObject = null;
                error = "glb_instantiate:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static void BuildNode(
            Parsed parsed,
            int index,
            Transform parent,
            IReadOnlyList<Material> materials,
            IDictionary<int, List<GameObject>> meshCache,
            ISet<int> stack)
        {
            JArray nodes = (JArray)parsed.Root["nodes"];
            if (nodes == null || index < 0 || index >= nodes.Count ||
                !stack.Add(index))
                throw new InvalidOperationException("node_invalid");
            JToken node = nodes[index];
            var go = new GameObject(
                node.Value<string>("name") ?? ("GLB Node " + index));
            go.transform.SetParent(parent, false);
            ApplyTransform(go.transform, node);
            if (node["mesh"] != null)
                BuildMesh(
                    parsed,
                    node.Value<int>("mesh"),
                    go.transform,
                    materials,
                    meshCache);
            foreach (JToken child in (JArray)node["children"] ?? new JArray())
                BuildNode(
                    parsed,
                    child.Value<int>(),
                    go.transform,
                    materials,
                    meshCache,
                    stack);
            stack.Remove(index);
        }

        private static void ApplyTransform(Transform transform, JToken node)
        {
            JArray matrix = node["matrix"] as JArray;
            if (matrix != null && matrix.Count == 16)
            {
                var m = new Matrix4x4();
                for (int column = 0; column < 4; column++)
                    for (int row = 0; row < 4; row++)
                        m[row, column] = matrix[column * 4 + row].Value<float>();
                Vector3 position = m.GetColumn(3);
                position.z = -position.z;
                Vector3 scale = m.lossyScale;
                Quaternion rotation = m.rotation;
                rotation = new Quaternion(-rotation.x, -rotation.y, rotation.z, rotation.w);
                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
                return;
            }
            transform.localPosition = ReadVector3(
                node["translation"], Vector3.zero, true);
            JArray rotationArray = node["rotation"] as JArray;
            if (rotationArray != null && rotationArray.Count == 4)
            {
                var rotation = new Quaternion(
                    -rotationArray[0].Value<float>(),
                    -rotationArray[1].Value<float>(),
                    rotationArray[2].Value<float>(),
                    rotationArray[3].Value<float>());
                transform.localRotation = rotation;
            }
            transform.localScale = ReadVector3(
                node["scale"], Vector3.one, false);
        }

        private static void BuildMesh(
            Parsed parsed,
            int meshIndex,
            Transform parent,
            IReadOnlyList<Material> materials,
            IDictionary<int, List<GameObject>> meshCache)
        {
            JArray meshes = (JArray)parsed.Root["meshes"];
            if (meshes == null || meshIndex < 0 || meshIndex >= meshes.Count)
                throw new InvalidOperationException("mesh_invalid");
            JArray primitives = (JArray)meshes[meshIndex]["primitives"];
            if (primitives == null) return;
            int part = 0;
            foreach (JToken primitive in primitives)
            {
                JObject attributes = (JObject)primitive["attributes"];
                Vector3[] vertices = ReadVector3Accessor(
                    parsed, attributes.Value<int>("POSITION"), true);
                Vector3[] normals = attributes["NORMAL"] == null
                    ? null
                    : ReadVector3Accessor(
                        parsed, attributes.Value<int>("NORMAL"), true);
                Vector2[] uv = attributes["TEXCOORD_0"] == null
                    ? null
                    : ReadVector2Accessor(
                        parsed, attributes.Value<int>("TEXCOORD_0"));
                int[] indices = primitive["indices"] == null
                    ? Sequential(vertices.Length)
                    : ReadIndices(parsed, primitive.Value<int>("indices"));
                Array.Reverse(indices);
                var mesh = new Mesh
                {
                    name = "GLB Mesh " + meshIndex + ":" + part,
                    indexFormat = vertices.Length > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16,
                    vertices = vertices,
                    triangles = indices,
                };
                if (uv != null && uv.Length == vertices.Length) mesh.uv = uv;
                if (normals != null && normals.Length == vertices.Length)
                    mesh.normals = normals;
                else
                    mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var partGo = new GameObject(mesh.name);
                partGo.transform.SetParent(parent, false);
                partGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                int materialIndex = primitive.Value<int?>("material") ?? -1;
                Material material =
                    materialIndex >= 0 && materialIndex < materials.Count
                        ? materials[materialIndex]
                        : MakeMaterial(null, null);
                partGo.AddComponent<MeshRenderer>().sharedMaterial = material;
                part++;
            }
        }

        private static List<Material> BuildMaterials(
            Parsed parsed,
            Shader hologramShader)
        {
            var result = new List<Material>();
            JArray materials = (JArray)parsed.Root["materials"] ?? new JArray();
            var textureCache = new Dictionary<int, Texture2D>();
            foreach (JToken source in materials)
            {
                JObject pbr = source["pbrMetallicRoughness"] as JObject;
                Color colour = ReadColor(
                    pbr?["baseColorFactor"], new Color(.2f, .9f, 1f, .72f));
                Color emissive = ReadColor(
                    source["emissiveFactor"], new Color(.08f, .4f, .5f, 1f));
                Texture2D texture = null;
                int textureIndex =
                    pbr?["baseColorTexture"]?.Value<int?>("index") ?? -1;
                int emissionTextureIndex =
                    source["emissiveTexture"]?.Value<int?>("index") ?? -1;
                if (textureIndex >= 0)
                    texture = ReadTexture(parsed, textureIndex, textureCache);
                // Blender hologram exports commonly move the authored colour
                // texture to emissiveTexture and deliberately omit baseColorTexture.
                // Reuse it as the visible texture; otherwise richly textured GLBs
                // would become a flat cyan silhouette at runtime.
                if (texture == null && emissionTextureIndex >= 0)
                    texture = ReadTexture(
                        parsed,
                        emissionTextureIndex,
                        textureCache);
                float emissionStrength =
                    source["extensions"]?
                        ["KHR_materials_emissive_strength"]?
                        .Value<float?>("emissiveStrength") ?? 1f;
                result.Add(MakeMaterial(
                    hologramShader,
                    texture,
                    colour,
                    emissive,
                    emissionStrength));
            }
            return result;
        }

        private static Material MakeMaterial(
            Shader shader,
            Texture texture,
            Color? colour = null,
            Color? emission = null,
            float emissionStrength = 1f)
        {
            shader = shader ??
                Shader.Find("MLOmega/XREAL FreeGuy Mesh") ??
                Shader.Find("MLOmega/XREAL Runtime Unlit") ??
                Shader.Find("Unlit/Texture");
            var material = new Material(shader);
            Color tint = colour ?? new Color(.2f, .9f, 1f, .72f);
            if (shader != null &&
                shader.name == "MLOmega/XREAL FreeGuy Mesh")
                tint.a = Mathf.Clamp(Mathf.Min(tint.a, .62f), .18f, .62f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
            }
            Color glow = emission ?? tint * 1.5f;
            if (material.HasProperty("_GridColor"))
                material.SetColor("_GridColor", new Color(
                    glow.r, glow.g, glow.b, .28f));
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", glow);
            if (material.HasProperty("_EmissionStrength"))
                material.SetFloat(
                    "_EmissionStrength",
                    Mathf.Clamp(emissionStrength, 0f, 8f));
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Texture2D ReadTexture(
            Parsed parsed,
            int textureIndex,
            IDictionary<int, Texture2D> cache)
        {
            if (cache.TryGetValue(textureIndex, out Texture2D cached))
                return cached;
            JArray textures = (JArray)parsed.Root["textures"];
            JArray images = (JArray)parsed.Root["images"];
            if (textures == null || images == null ||
                textureIndex < 0 || textureIndex >= textures.Count)
                return null;
            int sourceIndex = textures[textureIndex].Value<int?>("source") ?? -1;
            if (sourceIndex < 0 || sourceIndex >= images.Count) return null;
            JToken image = images[sourceIndex];
            byte[] bytes = null;
            if (image["bufferView"] != null)
                bytes = ReadBufferView(
                    parsed, image.Value<int>("bufferView"));
            else
            {
                string uri = image.Value<string>("uri");
                int comma = uri?.IndexOf(',') ?? -1;
                if (comma > 0 && uri.StartsWith(
                        "data:image/", StringComparison.Ordinal))
                    bytes = Convert.FromBase64String(uri.Substring(comma + 1));
            }
            if (bytes == null || bytes.Length == 0) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            cache[textureIndex] = texture;
            return texture;
        }

        private static Vector3[] ReadVector3Accessor(
            Parsed parsed,
            int accessorIndex,
            bool flipZ)
        {
            float[] values = ReadFloatAccessor(parsed, accessorIndex, "VEC3", 3);
            var result = new Vector3[values.Length / 3];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Vector3(
                    values[i * 3],
                    values[i * 3 + 1],
                    flipZ ? -values[i * 3 + 2] : values[i * 3 + 2]);
            return result;
        }

        private static Vector2[] ReadVector2Accessor(
            Parsed parsed,
            int accessorIndex)
        {
            float[] values = ReadFloatAccessor(parsed, accessorIndex, "VEC2", 2);
            var result = new Vector2[values.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
            return result;
        }

        private static float[] ReadFloatAccessor(
            Parsed parsed,
            int accessorIndex,
            string expectedType,
            int width)
        {
            JToken accessor = GetAccessor(parsed.Root, accessorIndex);
            if (accessor.Value<int>("componentType") != 5126 ||
                accessor.Value<string>("type") != expectedType ||
                accessor["sparse"] != null)
                throw new InvalidOperationException("accessor_unsupported");
            int count = accessor.Value<int>("count");
            int viewIndex = accessor.Value<int>("bufferView");
            JToken view = GetBufferView(parsed.Root, viewIndex);
            int stride = view.Value<int?>("byteStride") ?? width * 4;
            if (stride < width * 4)
                throw new InvalidOperationException("accessor_stride_invalid");
            int offset = view.Value<int?>("byteOffset") ?? 0;
            offset += accessor.Value<int?>("byteOffset") ?? 0;
            var result = new float[count * width];
            for (int i = 0; i < count; i++)
                for (int n = 0; n < width; n++)
                    result[i * width + n] = BitConverter.ToSingle(
                        parsed.Bin, offset + i * stride + n * 4);
            return result;
        }

        private static int[] ReadIndices(Parsed parsed, int accessorIndex)
        {
            JToken accessor = GetAccessor(parsed.Root, accessorIndex);
            if (accessor.Value<string>("type") != "SCALAR" ||
                accessor["sparse"] != null)
                throw new InvalidOperationException("index_accessor_invalid");
            int component = accessor.Value<int>("componentType");
            int width = component == 5121 ? 1 : component == 5123 ? 2 :
                component == 5125 ? 4 : 0;
            if (width == 0) throw new InvalidOperationException("index_type_invalid");
            int count = accessor.Value<int>("count");
            JToken view = GetBufferView(
                parsed.Root, accessor.Value<int>("bufferView"));
            int stride = view.Value<int?>("byteStride") ?? width;
            int offset = (view.Value<int?>("byteOffset") ?? 0) +
                (accessor.Value<int?>("byteOffset") ?? 0);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int at = offset + i * stride;
                result[i] = width == 1
                    ? parsed.Bin[at]
                    : width == 2
                        ? BitConverter.ToUInt16(parsed.Bin, at)
                        : checked((int)BitConverter.ToUInt32(parsed.Bin, at));
            }
            return result;
        }

        private static byte[] ReadBufferView(Parsed parsed, int index)
        {
            JToken view = GetBufferView(parsed.Root, index);
            int offset = view.Value<int?>("byteOffset") ?? 0;
            int length = view.Value<int>("byteLength");
            if (offset < 0 || length <= 0 || offset + length > parsed.Bin.Length)
                throw new InvalidOperationException("buffer_view_bounds");
            var result = new byte[length];
            Buffer.BlockCopy(parsed.Bin, offset, result, 0, length);
            return result;
        }

        private static int AccessorCount(JObject root, int index) =>
            GetAccessor(root, index).Value<int>("count");

        private static JToken GetAccessor(JObject root, int index)
        {
            JArray accessors = (JArray)root["accessors"];
            if (accessors == null || index < 0 || index >= accessors.Count)
                throw new InvalidOperationException("accessor_missing");
            return accessors[index];
        }

        private static JToken GetBufferView(JObject root, int index)
        {
            JArray views = (JArray)root["bufferViews"];
            if (views == null || index < 0 || index >= views.Count)
                throw new InvalidOperationException("buffer_view_missing");
            return views[index];
        }

        private static int[] Sequential(int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = i;
            return result;
        }

        private static Vector3 ReadVector3(
            JToken token,
            Vector3 fallback,
            bool flipZ)
        {
            JArray values = token as JArray;
            if (values == null || values.Count != 3) return fallback;
            return new Vector3(
                values[0].Value<float>(),
                values[1].Value<float>(),
                (flipZ ? -1f : 1f) * values[2].Value<float>());
        }

        private static Color ReadColor(JToken token, Color fallback)
        {
            JArray values = token as JArray;
            if (values == null || values.Count < 3) return fallback;
            return new Color(
                values[0].Value<float>(),
                values[1].Value<float>(),
                values[2].Value<float>(),
                values.Count > 3 ? values[3].Value<float>() : fallback.a);
        }

        private static bool TryParse(
            byte[] bytes,
            out Parsed parsed,
            out string error)
        {
            parsed = null;
            error = string.Empty;
            try
            {
                if (bytes == null || bytes.Length < 28 ||
                    BitConverter.ToUInt32(bytes, 0) != GlbMagic ||
                    BitConverter.ToUInt32(bytes, 4) != 2 ||
                    BitConverter.ToUInt32(bytes, 8) != bytes.Length)
                {
                    error = "glb_header_invalid";
                    return false;
                }
                int cursor = 12;
                byte[] json = null;
                byte[] bin = null;
                while (cursor + 8 <= bytes.Length)
                {
                    int length = checked((int)BitConverter.ToUInt32(bytes, cursor));
                    uint type = BitConverter.ToUInt32(bytes, cursor + 4);
                    cursor += 8;
                    if (length < 0 || cursor + length > bytes.Length)
                    {
                        error = "glb_chunk_bounds";
                        return false;
                    }
                    if (type == JsonChunk && json == null)
                    {
                        json = new byte[length];
                        Buffer.BlockCopy(bytes, cursor, json, 0, length);
                    }
                    else if (type == BinChunk && bin == null)
                    {
                        bin = new byte[length];
                        Buffer.BlockCopy(bytes, cursor, bin, 0, length);
                    }
                    cursor += length;
                }
                if (json == null || bin == null)
                {
                    error = "glb_chunks_missing";
                    return false;
                }
                string text = Encoding.UTF8.GetString(json).TrimEnd('\0', ' ');
                var root = JObject.Parse(text);
                if (!(root["asset"] is JObject asset) ||
                    asset.Value<string>("version") != "2.0")
                {
                    error = "glb_version_invalid";
                    return false;
                }
                parsed = new Parsed { Root = root, Bin = bin };
                return true;
            }
            catch (Exception ex)
            {
                error = "glb_parse:" + ex.GetType().Name;
                return false;
            }
        }
    }
}
