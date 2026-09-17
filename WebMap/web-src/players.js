import ui, { createUi } from "./ui";
import websocket from "./websocket";
import map from "./map";
import constants from "./constants";

const playerMapIcons = {};
let followingPlayer;

const hasValidPosition = (player) => Number.isFinite(player.x) && Number.isFinite(player.z);

const formatNumber = (value) => {
    const number = Number(value);
    return Number.isFinite(number) && number >= 0 ? `${Math.round(number)}` : '—';
};

const formatHealth = (health, maxHealth) => `${formatNumber(health)} / ${formatNumber(maxHealth)}`;

const healthPercent = (health, maxHealth) => {
    const current = Number(health);
    const maximum = Number(maxHealth);
    if (!Number.isFinite(current) || !Number.isFinite(maximum) || maximum <= 0) return 0;
    return Math.min(100, Math.max(0, 100 * current / maximum));
};

const followPlayer = (playerMapIcon) => {
    if (followingPlayer) {
        followingPlayer.playerListEntry.el.classList.remove('selected');
    }
    if (playerMapIcon && playerMapIcon !== followingPlayer) {
        followingPlayer = playerMapIcon;
        followingPlayer.playerListEntry.el.classList.add('selected');
        ui.map.classList.remove('smooth');
        map.setFollowIcon(playerMapIcon);
        ui.topMessage.textContent = `Following ${followingPlayer.name}`;
        setTimeout(() => {
            ui.map.classList.add('smooth');
        }, 0);
    } else {
        followingPlayer = null;
        map.setFollowIcon(null);
        ui.map.classList.remove('smooth');
        ui.topMessage.textContent = '';
    }
};

const init = () => {
    websocket.addActionListener('players', (players) => {
        let currentPlayerIds = Object.keys(playerMapIcons);
        let newPlayerIds = players.map(player => { return player.id });
        currentPlayerIds.filter(id => !newPlayerIds.includes(id)).forEach((id) => {
            if (playerMapIcons[id] === followingPlayer) {
                followPlayer(null);
            }
            playerMapIcons[id].playerListEntry.el.remove();
            map.removeIcon(playerMapIcons[id]);
            delete playerMapIcons[id];
        });

        players.forEach((player) => {
            let playerMapIcon = playerMapIcons[player.id];
            if (!playerMapIcon) {
                // new player
                const playerListEntry = createUi(`
                    <div class="playerListEntry">
                        <div class="name" data-id="name"></div>
                        <div class="details" data-id="details">
                            <div class="hpBar" data-id="hpBar">
                                <div class="hp" data-id="hp"></div>
                            </div>
                            <div class="playerStat"><span class="statLabel">Health</span><span class="statValue" data-id="hpText"></span></div>
                            <div class="playerStat"><span class="statLabel">Stamina</span><span class="statValue" data-id="staminaText"></span></div>
                            <div class="playerStat"><span class="statLabel">Eitr</span><span class="statValue" data-id="eitrText"></span></div>
                            <div class="playerStat positionStatus" data-id="positionStatus"></div>
                        </div>
                    </div>
                `);
                playerListEntry.ui.name.textContent = player.name;
                playerMapIcon = {
                    ...player,
                    type: 'player',
                    text: player.name,
                    zIndex: 5,
                    playerListEntry,
                    markerAdded: false
                };
                playerMapIcons[player.id] = playerMapIcon;
                playerListEntry.el.addEventListener('click', () => {
                    if (playerMapIcon.markerAdded && !playerMapIcon.hidden) {
                        if (ui.playerListTut) {
                            ui.playerListTut.remove();
                            ui.playerListTut = undefined;
                        }
                        followPlayer(playerMapIcon);
                    }
                });

                ui.playerList.appendChild(playerListEntry.el);
            }

            const shouldShowMarker = !player.hidden && hasValidPosition(player);
            if (shouldShowMarker) {
                playerMapIcon.x = player.x;
                playerMapIcon.z = player.z;
                if (!playerMapIcon.markerAdded) {
                    playerMapIcon.hidden = false;
                    map.addIcon(playerMapIcon, false);
                    playerMapIcon.markerAdded = true;
                } else if (playerMapIcon.hidden) {
		        map.showIcon(playerMapIcon);
                }
            } else if (playerMapIcon.markerAdded && !playerMapIcon.hidden) {
                // Keep the list entry, but never show an old position when the
                // player hides their marker or the server cannot provide one.
                map.hideIcon(playerMapIcon);
                if (followingPlayer === playerMapIcon) {
                    followPlayer(null);
                }
            }

            playerMapIcon.lastUpdate = Date.now();
            playerMapIcon.flags = player.flags;
            playerMapIcon.playerListEntry.ui.hp.style.width = `${healthPercent(player.health, player.maxHealth)}%`;
            playerMapIcon.playerListEntry.ui.hpText.textContent = formatHealth(player.health, player.maxHealth);
            playerMapIcon.playerListEntry.ui.staminaText.textContent = formatResource(player.stamina);
            playerMapIcon.playerListEntry.ui.eitrText.textContent = formatResource(player.eitr);
            playerMapIcon.playerListEntry.ui.positionStatus.textContent = shouldShowMarker
                ? 'Position visible'
                : 'Position hidden in Valheim';

            if (shouldShowMarker) {
                map.explore(player.x, player.z);
            }
        });
        map.updateIcons();
    });
};

const formatResource = (value) => value === null || typeof value === 'undefined' ? '—' : formatNumber(value);

export default {
    init
};
