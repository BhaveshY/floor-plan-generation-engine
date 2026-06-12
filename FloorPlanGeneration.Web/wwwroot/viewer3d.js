// ---------------------------------------------------------------------------
// 3D MODEL VIEWER — the WebGL half of the "3D Model" tab. app.js computes the
// scene as plain data (boxes, prisms, floors, labels in model metres, z up);
// this module owns every three.js concern: materials, lights and shadows, the
// orbit and first-person walk cameras, room picking, and binary glTF export.
// Loaded lazily via dynamic import the first time the tab opens, from local
// vendored modules only (the page CSP is script-src 'self' — no CDN).
// ---------------------------------------------------------------------------
import * as THREE from "./vendor/three.module.js";
import { OrbitControls } from "./vendor/OrbitControls.js";
import { GLTFExporter } from "./vendor/GLTFExporter.js";

const EYE_HEIGHT = 1.62;
const WALK_SPEED = 2.6;
const LOOK_SPEED = 0.0023;
const PITCH_LIMIT = 1.45;

// Architectural material palette: plaster walls, a dark service core, wood and
// tile floor tones per zone, white-model furniture. Shared instances — meshes
// never clone these, so rebuilds only ever dispose geometry.
function buildMaterials() {
  const standard = (color, roughness, extra) =>
    new THREE.MeshStandardMaterial({ color, roughness, metalness: 0.02, ...(extra || {}) });
  const floor = (color) => standard(color, 0.66, { side: THREE.DoubleSide });
  return {
    wall: standard(0xf2eee5, 0.92),
    "wall-partition": standard(0xf7f4ec, 0.94),
    core: standard(0x42474d, 0.8),
    slab: standard(0xc8c4bb, 0.95, { side: THREE.DoubleSide }),
    furniture: standard(0xfbfbf8, 0.88),
    glass: new THREE.MeshPhysicalMaterial({
      color: 0xa8c8ce,
      roughness: 0.08,
      metalness: 0,
      transparent: true,
      opacity: 0.42,
      side: THREE.DoubleSide
    }),
    "floor-living": floor(0xcdab7e),
    "floor-bedroom": floor(0xd5b88f),
    "floor-kitchen": floor(0xc7b89d),
    "floor-bathroom": floor(0xb4c2c6),
    "floor-balcony": floor(0xa8b3a4),
    "floor-corridor": floor(0xc2bcae),
    "floor-service": floor(0xbcb6a9),
    "floor-general": floor(0xc9b18a),
    highlight: new THREE.MeshBasicMaterial({ color: 0x2563eb, transparent: true, opacity: 0.28, side: THREE.DoubleSide })
  };
}

function ringShape(points) {
  const shape = new THREE.Shape();
  points.forEach((p, index) => {
    if (index === 0) {
      shape.moveTo(p.x, p.y);
    } else {
      shape.lineTo(p.x, p.y);
    }
  });
  shape.closePath();
  return shape;
}

function prismGeometry(points, z0, z1) {
  const geometry = new THREE.ExtrudeGeometry(ringShape(points), { depth: Math.max(z1 - z0, 0.01), bevelEnabled: false });
  geometry.translate(0, 0, z0);
  return geometry;
}

function boxMesh(box, material) {
  const geometry = new THREE.BoxGeometry(
    Math.max(box.x1 - box.x0, 0.01),
    Math.max(box.y1 - box.y0, 0.01),
    Math.max(box.z1 - box.z0, 0.01));
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.set((box.x0 + box.x1) / 2, (box.y0 + box.y1) / 2, (box.z0 + box.z1) / 2);
  return mesh;
}

// Room name tags as billboard sprites drawn onto canvas textures: always
// camera-facing, occluded naturally by walls during a walkthrough.
function labelSprite(text) {
  const scale = 4;
  const canvas = document.createElement("canvas");
  const context = canvas.getContext("2d");
  const font = `600 ${12 * scale}px "Segoe UI", system-ui, sans-serif`;
  context.font = font;
  const width = Math.ceil(context.measureText(text).width) + 16 * scale;
  const height = 22 * scale;
  canvas.width = width;
  canvas.height = height;
  context.font = font;
  context.textAlign = "center";
  context.textBaseline = "middle";
  const radius = height / 2;
  context.beginPath();
  context.moveTo(radius, 0);
  context.lineTo(width - radius, 0);
  context.arc(width - radius, radius, radius, -Math.PI / 2, Math.PI / 2);
  context.lineTo(radius, height);
  context.arc(radius, radius, radius, Math.PI / 2, -Math.PI / 2);
  context.closePath();
  context.fillStyle = "rgba(30, 34, 40, 0.78)";
  context.fill();
  context.fillStyle = "#f7f8f9";
  context.fillText(text, width / 2, height / 2 + scale);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  const material = new THREE.SpriteMaterial({ map: texture, transparent: true });
  const sprite = new THREE.Sprite(material);
  const spriteHeight = 0.42;
  sprite.scale.set(spriteHeight * (width / height), spriteHeight, 1);
  return sprite;
}

function disposeSubtree(root) {
  root.traverse((node) => {
    if (node.geometry) {
      node.geometry.dispose();
    }
    // Sprites own their material + texture (one per label); meshes share the
    // palette materials, which must survive scene rebuilds.
    if (node.isSprite && node.material) {
      if (node.material.map) {
        node.material.map.dispose();
      }
      node.material.dispose();
    }
  });
  while (root.children.length > 0) {
    root.remove(root.children[0]);
  }
}

export function createViewer(host, options) {
  const callbacks = options || {};
  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance" });
  } catch (error) {
    host.classList.add("viewer-unavailable");
    if (callbacks.onStatus) {
      callbacks.onStatus("3D view needs WebGL, which this browser blocked");
    }
    return null;
  }
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.08;
  renderer.domElement.classList.add("viewer-canvas");
  renderer.domElement.setAttribute("tabindex", "0");
  host.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xe9ebed);

  const materials = buildMaterials();
  const modelRoot = new THREE.Group();
  modelRoot.name = "FloorPlanModel";
  modelRoot.rotation.x = -Math.PI / 2;
  const labelRoot = new THREE.Group();
  labelRoot.rotation.x = -Math.PI / 2;
  scene.add(modelRoot);
  scene.add(labelRoot);

  const hemisphere = new THREE.HemisphereLight(0xffffff, 0x9b968c, 1.05);
  scene.add(hemisphere);
  const sun = new THREE.DirectionalLight(0xfff1dc, 2.4);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0004;
  scene.add(sun);
  scene.add(sun.target);

  const ground = new THREE.Mesh(
    new THREE.CircleGeometry(60, 64),
    new THREE.MeshStandardMaterial({ color: 0xdfe1e3, roughness: 1, metalness: 0 }));
  ground.rotation.x = -Math.PI / 2;
  ground.receiveShadow = true;
  scene.add(ground);

  const camera = new THREE.PerspectiveCamera(45, 1.6, 0.05, 500);
  camera.position.set(16, 14, 16);
  const orbit = new OrbitControls(camera, renderer.domElement);
  orbit.enableDamping = true;
  orbit.dampingFactor = 0.08;
  orbit.maxPolarAngle = Math.PI * 0.495;
  orbit.target.set(0, 0.6, 0);

  const clock = new THREE.Clock();
  const raycaster = new THREE.Raycaster();
  const pointer = new THREE.Vector2();
  const floorMeshes = [];
  const keysHeld = new Set();
  const walk = { yaw: 0, pitch: -0.08, dragging: false, lastX: 0, lastY: 0 };
  let cameraMode = "orbit";
  let active = false;
  let frameHandle = 0;
  let lastScene = null;
  let framedName = "";
  let walkBounds = { x: 24, z: 24 };
  let pickStart = null;

  function resize() {
    const width = Math.max(host.clientWidth, 1);
    const height = Math.max(host.clientHeight, 1);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    renderer.setSize(width, height, false);
  }
  const observer = new ResizeObserver(() => {
    resize();
    renderFrame();
  });
  observer.observe(host);
  resize();

  function frameScene(bounds) {
    const span = Math.max(bounds.width, bounds.height, 8);
    camera.position.set(span * 0.92, span * 0.78, span * 0.92);
    orbit.target.set(0, 0.6, 0);
    orbit.minDistance = 2;
    orbit.maxDistance = span * 3.2;
    orbit.update();
    sun.position.set(span * 0.8, span * 1.15, span * 0.45);
    sun.target.position.set(0, 0, 0);
    const shadowSpan = span * 0.85;
    sun.shadow.camera.left = -shadowSpan;
    sun.shadow.camera.right = shadowSpan;
    sun.shadow.camera.top = shadowSpan;
    sun.shadow.camera.bottom = -shadowSpan;
    sun.shadow.camera.far = span * 4;
    sun.shadow.camera.updateProjectionMatrix();
    ground.geometry.dispose();
    ground.geometry = new THREE.CircleGeometry(span * 4, 64);
    ground.position.y = -(lastScene && lastScene.slabDepth ? lastScene.slabDepth : 0.35) - 0.02;
    scene.fog = new THREE.Fog(0xe9ebed, span * 2.6, span * 6.5);
  }

  function setScene(data) {
    if (!data) {
      return;
    }
    lastScene = data;
    disposeSubtree(modelRoot);
    disposeSubtree(labelRoot);
    floorMeshes.length = 0;

    const cx = data.bounds.minX + data.bounds.width / 2;
    const cy = data.bounds.minY + data.bounds.height / 2;
    walkBounds = { x: data.bounds.width / 2 + 4, z: data.bounds.height / 2 + 4 };
    const offset = new THREE.Group();
    offset.position.set(-cx, -cy, 0);
    modelRoot.add(offset);
    const labelOffset = new THREE.Group();
    labelOffset.position.set(-cx, -cy, 0);
    labelRoot.add(labelOffset);

    (data.prisms || []).forEach((prism) => {
      const mesh = new THREE.Mesh(prismGeometry(prism.points, prism.z0, prism.z1), materials[prism.kind] || materials.slab);
      mesh.castShadow = prism.kind !== "slab";
      mesh.receiveShadow = true;
      offset.add(mesh);
    });
    (data.floors || []).forEach((floorData) => {
      const mesh = new THREE.Mesh(prismGeometry(floorData.points, 0.005, 0.05), materials[floorData.kind] || materials["floor-general"]);
      mesh.receiveShadow = true;
      mesh.userData.roomId = floorData.roomId || "";
      mesh.userData.roomName = floorData.name || "";
      offset.add(mesh);
      if (floorData.roomId) {
        floorMeshes.push(mesh);
      }
      if (data.selectedRoomId && floorData.roomId === data.selectedRoomId) {
        const halo = new THREE.Mesh(prismGeometry(floorData.points, 0.055, 0.075), materials.highlight);
        offset.add(halo);
      }
    });
    (data.boxes || []).forEach((box) => {
      const mesh = boxMesh(box, materials[box.kind] || materials.wall);
      mesh.castShadow = box.kind !== "glass";
      mesh.receiveShadow = true;
      offset.add(mesh);
    });
    (data.labels || []).forEach((label) => {
      const sprite = labelSprite(label.text);
      sprite.position.set(label.x, label.y, label.z);
      labelOffset.add(sprite);
    });

    if (framedName !== data.name) {
      framedName = data.name;
      frameScene(data.bounds);
    }
    renderFrame();
  }

  function walkDirection() {
    let forward = 0;
    let strafe = 0;
    if (keysHeld.has("w") || keysHeld.has("arrowup")) {
      forward += 1;
    }
    if (keysHeld.has("s") || keysHeld.has("arrowdown")) {
      forward -= 1;
    }
    if (keysHeld.has("d") || keysHeld.has("arrowright")) {
      strafe += 1;
    }
    if (keysHeld.has("a") || keysHeld.has("arrowleft")) {
      strafe -= 1;
    }
    return { forward, strafe };
  }

  function updateWalk(delta) {
    const { forward, strafe } = walkDirection();
    camera.rotation.order = "YXZ";
    camera.rotation.y = walk.yaw;
    camera.rotation.x = walk.pitch;
    if (forward !== 0 || strafe !== 0) {
      const speed = WALK_SPEED * (keysHeld.has("shift") ? 2 : 1) * delta;
      const sin = Math.sin(walk.yaw);
      const cos = Math.cos(walk.yaw);
      camera.position.x += (-sin * forward + cos * strafe) * speed;
      camera.position.z += (-cos * forward - sin * strafe) * speed;
      camera.position.x = Math.max(-walkBounds.x, Math.min(walkBounds.x, camera.position.x));
      camera.position.z = Math.max(-walkBounds.z, Math.min(walkBounds.z, camera.position.z));
    }
    camera.position.y = EYE_HEIGHT;
  }

  function renderFrame() {
    renderer.render(scene, camera);
  }

  function tick() {
    frameHandle = active ? requestAnimationFrame(tick) : 0;
    const delta = Math.min(clock.getDelta(), 0.1);
    if (cameraMode === "walk") {
      updateWalk(delta);
    } else {
      orbit.update();
    }
    renderFrame();
  }

  function setActive(next) {
    const value = Boolean(next);
    if (value === active) {
      return;
    }
    active = value;
    if (active) {
      clock.getDelta();
      frameHandle = requestAnimationFrame(tick);
    } else if (frameHandle) {
      cancelAnimationFrame(frameHandle);
      frameHandle = 0;
      if (cameraMode === "walk") {
        setCameraMode("orbit");
      }
    }
  }

  function setCameraMode(mode) {
    const next = mode === "walk" ? "walk" : "orbit";
    if (next === cameraMode) {
      return;
    }
    cameraMode = next;
    if (next === "walk") {
      orbit.enabled = false;
      walk.yaw = Math.atan2(-(orbit.target.x - camera.position.x), -(orbit.target.z - camera.position.z));
      walk.pitch = -0.05;
      camera.position.y = EYE_HEIGHT;
      // Pointer lock gives console-style mouse look; embedded browsers that
      // refuse it quietly fall back to drag-to-look on the same handlers.
      if (renderer.domElement.requestPointerLock) {
        try {
          const request = renderer.domElement.requestPointerLock();
          if (request && request.catch) {
            request.catch(() => {});
          }
        } catch (error) { /* drag-look fallback */ }
      }
      renderer.domElement.focus();
    } else {
      orbit.enabled = true;
      if (document.pointerLockElement === renderer.domElement && document.exitPointerLock) {
        document.exitPointerLock();
      }
      const span = lastScene ? Math.max(lastScene.bounds.width, lastScene.bounds.height, 8) : 18;
      orbit.target.set(0, 0.6, 0);
      if (camera.position.y < 3) {
        camera.position.set(span * 0.92, span * 0.78, span * 0.92);
      }
      orbit.update();
    }
    if (callbacks.onModeChange) {
      callbacks.onModeChange(cameraMode);
    }
  }

  function pickRoom(event) {
    const rect = renderer.domElement.getBoundingClientRect();
    pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(pointer, camera);
    const hits = raycaster.intersectObjects(floorMeshes, false);
    if (callbacks.onPickRoom) {
      callbacks.onPickRoom(hits.length > 0 ? hits[0].object.userData.roomId : "");
    }
  }

  renderer.domElement.addEventListener("pointerdown", (event) => {
    pickStart = { x: event.clientX, y: event.clientY, time: performance.now() };
    if (cameraMode === "walk" && document.pointerLockElement !== renderer.domElement) {
      walk.dragging = true;
      walk.lastX = event.clientX;
      walk.lastY = event.clientY;
    }
  });
  window.addEventListener("pointerup", (event) => {
    const start = pickStart;
    pickStart = null;
    walk.dragging = false;
    if (cameraMode !== "orbit" || !start) {
      return;
    }
    const still = Math.abs(event.clientX - start.x) < 5 && Math.abs(event.clientY - start.y) < 5;
    if (still && performance.now() - start.time < 400 && event.target === renderer.domElement) {
      pickRoom(event);
    }
  });
  document.addEventListener("pointermove", (event) => {
    if (cameraMode !== "walk") {
      return;
    }
    if (document.pointerLockElement === renderer.domElement) {
      walk.yaw -= event.movementX * LOOK_SPEED;
      walk.pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, walk.pitch - event.movementY * LOOK_SPEED));
    } else if (walk.dragging) {
      walk.yaw -= (event.clientX - walk.lastX) * LOOK_SPEED * 1.4;
      walk.pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, walk.pitch - (event.clientY - walk.lastY) * LOOK_SPEED * 1.4));
      walk.lastX = event.clientX;
      walk.lastY = event.clientY;
    }
  });
  document.addEventListener("keydown", (event) => {
    if (!active || cameraMode !== "walk") {
      return;
    }
    if (event.key === "Escape") {
      setCameraMode("orbit");
      return;
    }
    keysHeld.add(event.key.toLowerCase());
    if (["w", "a", "s", "d", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(event.key.toLowerCase())) {
      event.preventDefault();
    }
  });
  document.addEventListener("keyup", (event) => {
    keysHeld.delete(event.key.toLowerCase());
  });
  document.addEventListener("pointerlockchange", () => {
    if (cameraMode === "walk" && document.pointerLockElement !== renderer.domElement) {
      // Esc released the pointer: surface the state change in the toolbar.
      setCameraMode("orbit");
    }
  });

  function exportGlb() {
    if (!lastScene) {
      if (callbacks.onStatus) {
        callbacks.onStatus("Generate a plan before exporting a model");
      }
      return;
    }
    const exporter = new GLTFExporter();
    exporter.parse(
      modelRoot,
      (result) => {
        const blob = new Blob([result], { type: "model/gltf-binary" });
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = `${lastScene.name || "floor-plan"}.glb`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(link.href);
        if (callbacks.onStatus) {
          callbacks.onStatus("3D model saved as .glb");
        }
      },
      (error) => {
        if (callbacks.onStatus) {
          callbacks.onStatus("Model export failed");
        }
        console.error("GLTF export failed", error);
      },
      { binary: true });
  }

  function dispose() {
    setActive(false);
    observer.disconnect();
    disposeSubtree(modelRoot);
    disposeSubtree(labelRoot);
    orbit.dispose();
    renderer.dispose();
    renderer.domElement.remove();
  }

  return {
    setScene,
    setActive,
    setCameraMode,
    cameraMode: () => cameraMode,
    exportGlb,
    dispose
  };
}
