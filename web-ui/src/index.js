var Viewport = require('pixi-viewport')
var PIXI = require('pixi.js')
var firebase = require("firebase")
var firestore = require("firebase/firestore")

firebase.initializeApp({
  apiKey: 'AIzaSyBPza7LIiAnw0XZcoh9qJTjDXmI9q2El2U',
  authDomain: 'ori-tracker.firebaseapp.com',
  projectId: 'ori-tracker'
});

// Initialize Cloud Firestore through Firebase
var db = firebase.firestore();

window.db = db

var traces = {}


let type = "WebGL"
if(!PIXI.utils.isWebGLSupported()){
  type = "canvas"
}

PIXI.utils.sayHello(type)
PIXI.loader
  .add("images/ori-map.jpg")
  .load(setup);

let app = new PIXI.Application({
  antialias: true
});
app.renderer.view.style.position = "absolute";
app.renderer.view.style.display = "block";
app.renderer.autoResize = true;

function resize() {
  app.renderer.resize(window.innerWidth, window.innerHeight);
}

resize()
window.onresize = resize


document.body.appendChild(app.view);

function point(x, y) {
  return {x: x, y: y};
}

let texture = PIXI.utils.TextureCache["images/ori-map.jpg"];

let swampTeleporter = point(493.719818, -74.31961);
let gladesTeleporter = point(109.90181, -257.681549);
let swampTeleporterOnMap = point(4523, 2867);
let gladesTeleporterOnMap = point(3438, 3384);

function toMapCoord(gameCoords) {
    var map1 = gladesTeleporterOnMap;
    var map2 = swampTeleporterOnMap;
    var game1 = gladesTeleporter;
    var game2 = swampTeleporter;

    var gameLeftSide = game2.x - ((map2.x / (map2.x - map1.x)) * (game2.x - game1.x));
    var gameTopSide = game2.y - ((map2.y / (map2.y - map1.y)) * (game2.y - game1.y));

    var scalex = (swampTeleporter.x - gladesTeleporter.x) / (swampTeleporterOnMap.x - gladesTeleporterOnMap.x);
    var scaley = (swampTeleporter.y - gladesTeleporter.y) / (swampTeleporterOnMap.y - gladesTeleporterOnMap.y);
    var mapx = (gameCoords.x - gameLeftSide) / scalex;
    var mapy = (gameCoords.y - gameTopSide) / scaley;

  return point(mapx, mapy)
}


function setup() {
  let mapTexture = PIXI.loader.resources["images/ori-map.jpg"].texture
  let mapImage = new PIXI.Sprite(mapTexture);
  let viewport = new Viewport({
    screenWidth: window.innerWidth,
    screenHeight: window.innerHeight,
    worldWidth: mapTexture.width,
    worldHeight: mapTexture.heigh
  })

  viewport
    .drag()
    .wheel()
    .clamp()
    .clampZoom({
      minWidth: mapTexture.width / 12,
      maxWidth: mapTexture.width * 1.5
    });

  viewport.moveCenter(3438, 3384);

  let trace = new PIXI.Graphics();

  app.stage.addChild(viewport);
  viewport.addChild(mapImage);
  viewport.addChild(trace);

  var ticker = PIXI.ticker.shared;
  ticker.add((deltaTime) => {

  })
  ticker.start();

  db
    .collection('boards/abc123/players/xavier/traces')
    .onSnapshot(function(snapshot) {
      traces = {}
      trace.clear()
      trace.lineStyle(2, 0xff0000);
      snapshot.forEach(function(doc) {
        var data = doc.data();
        var x = toMapCoord(data)
        if (Object.keys(traces).length == 0) {
          trace.moveTo(x.x, x.y)
        } else {
          trace.lineTo(x.x, x.y)
        }
        traces[doc.id] = data
      })
    })
}
