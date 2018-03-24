const functions = require('firebase-functions');
const admin = require('firebase-admin');
const app = require('express')();
const crypto = require('crypto');

admin.initializeApp(functions.config().firebase);
var db = admin.firestore()

app.post("/:board_id", (req, res) => {
  var batch = db.batch();
  var board = req.params.board_id
  var data = req.body;
  var player = data.playerId;
  var playerName = data.playerName;
  var playerHash = crypto.createHmac('sha256', player)
                     .digest('hex');

  var playerPath = "boards/" + board + "/players/" + playerHash
  batch.set(db.doc(playerPath), {name: playerName})

  data.events.forEach(function(x) {
    var id = x.timestamp
    var doc = {
      x: x.x,
      y: x.y,
      start: x.start
    }

    var path = playerPath + "/traces/" + id

    batch.set(db.doc(path), doc);
  })

  return batch.commit().then(function() {
    return res.send("Ok");
  })
});

exports.track = functions.https.onRequest(app);
