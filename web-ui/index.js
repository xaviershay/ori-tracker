var Viewport = require('pixi-viewport')
var PIXI = require('pixi.js')

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

let texture = PIXI.utils.TextureCache["images/ori-map.jpg"];

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

  app.stage.pivot.x = 100
  let trace = new PIXI.Graphics();

  trace.clear();
  trace.lineStyle(2, 0xff0000);
  trace.moveTo(0, 0);
  trace.lineTo(200, 300);
  trace.lineTo(300, 400);

  app.stage.addChild(viewport);
  viewport.addChild(mapImage);
  viewport.addChild(trace);

  var ticker = PIXI.ticker.shared;
  ticker.add((deltaTime) => {
    /*
    app.stage.pivot.x += deltaTime
    app.stage.pivot.y += deltaTime
    */
  })
  ticker.start();
}
