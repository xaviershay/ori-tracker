import React from 'react';
import ReactDOM from 'react-dom';
import './index.css';
import './bootstrap.cyborg.min.css';
import registerServiceWorker from './registerServiceWorker';
import firebase from 'firebase'
import firestore from '@firebase/firestore'
import { Map, ImageOverlay, TileLayer, Polyline } from 'react-leaflet';
import Leaflet from 'leaflet';
import { withRouter, Route } from 'react-router';
import { BrowserRouter, Link } from 'react-router-dom';
import RasterCoords from 'leaflet-rastercoords';
import uuid from 'uuid-encoded';
import { Button } from 'reactstrap';

firebase.initializeApp({
  apiKey: 'AIzaSyBPza7LIiAnw0XZcoh9qJTjDXmI9q2El2U',
  authDomain: 'ori-tracker.firebaseapp.com',
  projectId: 'ori-tracker'
});
var db = firebase.firestore();


function point(x, y) {
  return {x: x, y: y};
}

let swampTeleporter = point(493.719818, -74.31961);
let gladesTeleporter = point(109.90181, -257.681549);
//let swampTeleporterOnMap = point(4523, 2867);
//let gladesTeleporterOnMap = point(3438, 3384);

// Pretty close, not exact
let swampTeleporterOnMap = point(15215, 9576);
let gladesTeleporterOnMap = point(11854, 11174);

var map1 = gladesTeleporterOnMap;
var map2 = swampTeleporterOnMap;
var game1 = gladesTeleporter;
var game2 = swampTeleporter;

var mapRightSide = 20480;
var mapBottomSide = 14592;

var gameLeftSide = game2.x - ((map2.x / (map2.x - map1.x)) * (game2.x - game1.x));
var gameTopSide = game2.y - ((map2.y / (map2.y - map1.y)) * (game2.y - game1.y));

var scalex = (swampTeleporter.x - gladesTeleporter.x) / (swampTeleporterOnMap.x - gladesTeleporterOnMap.x);
var scaley = (swampTeleporter.y - gladesTeleporter.y) / (swampTeleporterOnMap.y - gladesTeleporterOnMap.y);

var gameRightSide = mapRightSide / map1.x * (game1.x - gameLeftSide) + gameLeftSide
var gameBottomSide = mapBottomSide / map1.y * (game1.y - gameTopSide) + gameTopSide

function distance(a, b) {
  return Math.sqrt((b.x - a.x) ** 2 + (b.y - a.y) ** 2)
}

const mapCenter = [0, 0];
const zoomLevel = 3;
const bounds = [[gameBottomSide, gameLeftSide], [gameTopSide, gameRightSide]];

// Work-around for lines between tiles on fractional zoom levels
// https://github.com/Leaflet/Leaflet/issues/3575
(function(){
    var originalInitTile = Leaflet.GridLayer.prototype._initTile
    Leaflet.GridLayer.include({
        _initTile: function (tile) {
            originalInitTile.call(this, tile);

            var tileSize = this.getTileSize();

            tile.style.width = tileSize.x + 1 + 'px';
            tile.style.height = tileSize.y + 1 + 'px';
        }
    });
})()

var h = mapBottomSide;
var w = mapRightSide;

var rc = {
  unproject: function() { return [0, 0] }
}
// Integrate leaflet-rastercoords per https://github.com/PaulLeCam/react-leaflet/issues/410
class MapExtended extends Map {
  createLeafletElement(props) {
    let LeafletMapElement = super.createLeafletElement(props);
    let img = [
      w, // original width of image `karta.jpg`
      h  // original height of image
    ]

    // assign map and image dimensions
    rc = new RasterCoords(LeafletMapElement, img)

    // set the view on a marker ...
    LeafletMapElement.setView(rc.unproject([h / 2, w / 2]), zoomLevel)

    return LeafletMapElement;
  }
}

class MapView extends React.Component {
  constructor() {
    super()
    this.state = {traces: []}
  }
  componentWillMount() {
    var me = this;
    db
      .collection('boards/abc123/players/xavier/traces')
      .onSnapshot(function(snapshot) {
        var maxContinuous = 50; // TODO: Figure out what makes sense here
        var polyline = []
        var traces = []
        var lastPos = null;
        var teleportColor = "lime";
        var teleportOpacity = 0.5;
        var traceColor = "red";
        var traceOpacity = 0.7;
        snapshot.forEach(function(doc) {
          var data = doc.data();
          if (lastPos == null || data.start) {
            if (polyline.length > 0) {
              traces.push({color: traceColor, line: polyline, opacity: traceOpacity})
            }

            polyline = []
          } else if (distance(lastPos, data) > maxContinuous) {
            traces.push({color: traceColor, line: polyline, opacity: traceOpacity})
            traces.push({color: teleportColor, line: [polyline[polyline.length - 1], rc.unproject(toMapCoord([data.y, data.x]))], opacity: teleportOpacity});
            polyline = []
          }
          polyline.push(rc.unproject(toMapCoord([data.y, data.x])));
          lastPos = data;
        })
        if (polyline.length > 0) {
          traces.push({color: traceColor, line: polyline, opacity: traceOpacity})
        }

        me.setState({traces: traces})
      })
  }

      /*<!--<ImageOverlay url='/images/ori-map.jpg' bounds={ bounds } />-->*/
  render() {
    return <MapExtended
      minZoom={0} maxZoom={7} center={[0,0]}
    >
      <TileLayer url='/images/ori-map/{z}/{x}/{y}.png' noWrap='true'  />
      { this.state.traces.map((trace, idx) => <Polyline key={idx} color={trace.color} opacity={trace.opacity} positions={trace.line} />) }
    </MapExtended>
  }
}

function trackerUrl(data) {
  var host = "";
  if (window.location.hostname == "localhost") {
    host = "http://localhost:5000/ori-tracker/us-central1"
  } else {
    host = "TODO"
  }
  return host + "/track?board_id=" + data.publicId
}

function viewerUrl(data) {
  var location = window.location;
  return location.protocol + "//" + location.host + "/map/" + data.publicId
}

const Board = ({match}) => {
  return (
    <div className="board-container">
      <p/>
       <div className="container">
          <div className="form-group row">
            <label htmlFor="tracker-url-input" className="col-sm-2 col-form-label">Tracker URL</label>
            <div className="col-sm-10">
              <input type="text" readOnly={true} id="tracker-url-input" value={trackerUrl(match.params)} className="form-control" />
              <p className="hint">Paste this URL into the Tracker field of all clients who should be reporting to this map.</p>
            </div>
          </div>
          <div className="form-group row">
            <label htmlFor="viewer-url-input" className="col-sm-2 col-form-label">Viewer URL</label>
            <div className="col-sm-10">
              <input type="text" readOnly={true} id="viewer-url-input" value={viewerUrl(match.params)} className="form-control" />
              <p className="hint">Share with friends so they can watch, but won't be able to contribute.</p>
            </div>
          </div>
        </div>
        <MapView />
      </div>
  )
}

const Header = () => {
  return(
    <header>
      <div className="navbar navbar-dark bg-dark box-shadow">
        <div className="container d-flex justify-content-between">
          <a href="/" className="navbar-brand d-flex align-items-center">
            <strong>Ori Tracker</strong>
          </a>
          <button className="navbar-toggler" type="button" data-toggle="collapse" data-target="#navbarHeader" aria-controls="navbarHeader" aria-expanded="false" aria-label="Toggle navigation">
            <span className="navbar-toggler-icon"></span>
          </button>
        </div>
      </div>
    </header>
  )
}

class MainPage extends React.Component {
  newBoard(history) {
    var publicBoardId = uuid();
    var privateBoardId = uuid();

    db.doc('boards/' + publicBoardId).set({
      createdAt: firebase.firestore.FieldValue.serverTimestamp(),
      privateId: privateBoardId
    });

    history.push("/map/" + publicBoardId)
  }

  render() {
    return(
      <section className='jumbotron text-center'>
        <div className='container text-center'>
          <h1 className='jumbotron-heading'>Ori Tracker</h1>
          <p className='lead text-muted'>Track you and your friends' Ori and the Blind Forest movement on a shared realtime map.</p>
          <p>
            <a href='#' className='btn btn-primary my-2'>Download Client</a>&nbsp;
            <a href='#' onClick={() => this.newBoard(this.props.history)} className='btn btn-primary my-2'>New Map</a>
          </p>
        </div>
      </section>
    )
  }
}

/*
class Game extends React.Component {
  constructor(props) {
    super(props)
    this.state = { traces: [] }
  }

  render() {
    return (
      <div className="game">
        <h1>Ori Tracker</h1>
        <Route path="/" component={MainPage} />
        <Route path="/map/:publicId" component={Board} />
      <ul>
        {
          this.state.traces.map( k => <li key={k.id}>{k.x},{k.y}</li> )
        }
      </ul>
      </div>
    );
  }
}
*/

/*
        <main role="main">
          <Route path='/' component={MainPage} />
          <Route path="/map/:publicId" component={Board} />
        </main>
        */
ReactDOM.render(
    <BrowserRouter>
    <div id="app">
        <Header />
        <Route path='/' exact={true} component={MainPage} />
        <Route path="/map/:publicId" component={Board} />
    </div>
    </BrowserRouter>,
    document.getElementById('root')
);
registerServiceWorker();

function toMapCoord(gameCoords) {
    gameCoords = {x: gameCoords[1], y: gameCoords[0] }
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

  return [mapx, mapy]; //point(mapx, mapy)
}

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
