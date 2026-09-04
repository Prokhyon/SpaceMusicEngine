import { useMemo } from "react";
import type { Coordinate, SpaceDefinition } from "../../model/types";
import { enumerateCoordinates, layerVolumeAt } from "../../model/types";

const MAX_ROWS = 4096;

interface TableViewProps {
  space: SpaceDefinition;
  selectedCoordinate: Coordinate | null;
  onSelect: (coordinate: Coordinate) => void;
}

export function TableView({ space, selectedCoordinate, onSelect }: TableViewProps) {
  const coordinates = useMemo(() => enumerateCoordinates(space.axes), [space.axes]);
  const selectedKey = selectedCoordinate?.join(",") ?? null;

  if (coordinates.length > MAX_ROWS) {
    return (
      <div className="table-warning">
        Trop de combinaisons ({coordinates.length}) pour la vue tableau. Réduisez le nombre de
        paliers ou utilisez la vue cube en tranches.
      </div>
    );
  }

  return (
    <div className="table-view">
      <table>
        <thead>
          <tr>
            {space.axes.map((axis) => (
              <th key={axis.axisId} className="axis-col">
                {axis.axisId}
              </th>
            ))}
            {space.layers.map((layer) => (
              <th key={layer.layerId}>{layer.layerId}</th>
            ))}
            {space.layers.length === 0 && <th className="empty-col">— aucune couche —</th>}
          </tr>
        </thead>
        <tbody>
          {coordinates.map((coordinate) => {
            const key = coordinate.join(",");
            const isSelected = key === selectedKey;
            return (
              <tr
                key={key}
                className={isSelected ? "selected" : undefined}
                onClick={() => onSelect(coordinate)}
              >
                {coordinate.map((v, i) => (
                  <td key={i} className="axis-col">
                    {v}
                  </td>
                ))}
                {space.layers.map((layer) => {
                  const volume = layerVolumeAt(layer, space, coordinate);
                  return (
                    <td key={layer.layerId} className={volume > 0 ? "cell-active" : "cell-inactive"}>
                      {volume >= 1 ? "●" : volume > 0 ? `${Math.round(volume * 100)}%` : ""}
                    </td>
                  );
                })}
                {space.layers.length === 0 && <td className="empty-col" />}
              </tr>
            );
          })}
        </tbody>
      </table>
      <p className="hint">
        Cliquez une ligne pour éditer les couches de cette combinaison dans le panneau de droite.
        L'activation d'une couche se règle via ses filtres d'axes (plages min/max).
      </p>
    </div>
  );
}
