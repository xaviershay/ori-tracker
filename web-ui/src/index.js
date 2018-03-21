import React from 'react';
import ReactDOM from 'react-dom';
import './index.css';
import registerServiceWorker from './registerServiceWorker';
import firebase from 'firebase'
import firestore from '@firebase/firestore'

firebase.initializeApp({
  apiKey: 'AIzaSyBPza7LIiAnw0XZcoh9qJTjDXmI9q2El2U',
  authDomain: 'ori-tracker.firebaseapp.com',
  projectId: 'ori-tracker'
});
var db = firebase.firestore();

class Game extends React.Component {
  constructor(props) {
    super(props)
    this.state = { traces: [] }
  }

  componentWillMount() {
    var me = this;
    db
      .collection('boards/abc123/players/xavier/traces')
      .onSnapshot(function(snapshot) {
        var maxContinuous = 50; // TODO: Figure out what makes sense here
        var traces = []
        var lastPos = null;
        snapshot.forEach(function(doc) {
          var data = doc.data();
          /*
          var x = toMapCoord(data)
          if (lastPos == null || data.start) {
            trace.moveTo(x.x, x.y)
          } else {
            if (distance(lastPos, x) > maxContinuous) {
              trace.lineStyle(2, 0x00ff00, 0.5);
            } else {
              trace.lineStyle(2, 0xff0000, 0.8);
            }
            trace.lineTo(x.x, x.y)
          }
          */
          data.id = doc.id;
          traces.push(data);
        })
        console.log(traces);
        me.setState({traces: traces})
      })
  }
  render() {
    return (
      <div className="game">
        <h1>Hello</h1>
      <ul>
        {
          this.state.traces.map( k => <li key={k.id}>{k.x},{k.y}</li> )
        }
      </ul>
      </div>
    );
  }
}

ReactDOM.render(
    <Game />,
    document.getElementById('root')
);
registerServiceWorker();

/*
var Viewport = require('pixi-viewport')
var PIXI = require('pixi.js')
var firebase = require("firebase")
var firestore = require("firebase/firestore")




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
//window.onresize = resize


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

function distance(a, b) {
  return Math.sqrt((b.x - a.x) ** 2 + (b.y - a.y) ** 2)
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
      var maxContinuous = 50; // TODO: Figure out what makes sense here
      traces = {}
      trace.clear()
      trace.lineStyle(2, 0xff0000);
      var lastPos = null;
      snapshot.forEach(function(doc) {
        var data = doc.data();
        var x = toMapCoord(data)
        if (lastPos == null || data.start) {
          trace.moveTo(x.x, x.y)
        } else {
          if (distance(lastPos, x) > maxContinuous) {
            trace.lineStyle(2, 0x00ff00, 0.5);
          } else {
            trace.lineStyle(2, 0xff0000, 0.8);
          }
          trace.lineTo(x.x, x.y)
        }
        traces[doc.id] = data
        lastPos = x
      })
    })
}
*/
